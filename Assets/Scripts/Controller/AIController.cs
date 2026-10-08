using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

public enum AIControlMode
{
    LocalPlanner = 0,
    MLAgents = 1,
}

public class AIController : PlayerController
{
    [SerializeField]
    private float actionInterval = 1.5f;
    [SerializeField, Range(200, 500)]
    private int searchIterations = 400;
    [SerializeField]
    private int searchTimeBudgetMs = 50;
    [SerializeField]
    private float explorationConstant = 1.4f;
    [SerializeField]
    private int rolloutActionLimit = 8;
    [SerializeField, Range(1, 3)]
    private int maxRootTurns = 2;
    [SerializeField]
    private bool enableAIDebugLog;
    [SerializeField]
    private CardListSO opponentBeliefPool;
    [SerializeField]
    private AIModelConfig modelConfig;
    [Header("Control Mode")]
    [SerializeField]
    private AIControlMode controlMode = AIControlMode.LocalPlanner;
    [Header("Hybrid AI")]
    [SerializeField]
    private bool enableLLMStrategy;
    [SerializeField]
    private LLMStrategyClient llmStrategyClient;

    [Header("LLM Call Policy")]
    [SerializeField, Min(1)]
    private int llmStrategyIntervalTurns = 3;
    [SerializeField, Range(0f, 1f)]
    private float llmConfidenceThreshold = 0.6f;
    [SerializeField, Range(0f, 1f)]
    private float llmDisadvantageHealthRatio = 0.5f;

    private float nextActionTime;
    private IAIPlanner planner;
    private bool actionInProgress;
    private int completedAiTurns;
    private bool loggedMLAgentsFallback;
    private CardBattleAgent mlAgentsAgent;

    public AIControlMode ControlMode => controlMode;
    public bool IsMLAgentsMode => controlMode == AIControlMode.MLAgents;

    public void ApplyTrainingCadence(float interval)
    {
        actionInterval = Mathf.Max(0.02f, interval);
        nextActionTime = 0f;
    }

    public void SetLLMStrategyEnabled(bool enabled)
    {
        enableLLMStrategy = enabled;
        enableAIDebugLog = false;
    }


    private void OnEnable()
    {
        IsAIControlled = true;
        actionInProgress = false;
        loggedMLAgentsFallback = false;
        SyncMLAgentsComponents();
    }

    private void OnDisable()
    {
        IsAIControlled = false;
        DisposePlanner();
        if (mlAgentsAgent != null)
        {
            mlAgentsAgent.enabled = false;
        }
        DecisionRequester requester = GetComponent<DecisionRequester>();
        if (requester != null)
        {
            requester.enabled = false;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SyncMLAgentsComponents();
    }
#endif

    private void Update()
    {
        SyncMLAgentsComponents();
        if (!ShouldProcessTurnActions() || actionInProgress || Time.time < nextActionTime)
        {
            return;
        }

        StartCoroutine(ProcessTurnAction());
    }

    private IEnumerator ProcessTurnAction()
    {
        actionInProgress = true;

        BattleManager battleManager = GM.Ins.BM;
        if (!SnapshotFactory.TryCreate(battleManager, GM.Ins.BM.players.IndexOf(this), out BattleStateSnapshot snapshot, out string error, opponentBeliefPool))
        {
            Debug.LogWarning($"[AI MCTS] Snapshot creation failed: {error}", this);
            ScheduleNextAction();
            actionInProgress = false;
            yield break;
        }

        List<SimulatedAction> legalActions = new BattleStateSimulator().GenerateLegalActions(snapshot);
        bool training = IsMLAgentsTraining();
        bool useMLAgents = ShouldUseMLAgentsPolicy();
        LLMStrategyHint hint = null;
        if (enableLLMStrategy
            && !training
            && ResolveLLMClient() != null
            && ShouldRequestLLMHint(snapshot, legalActions))
        {
            yield return llmStrategyClient.RequestHint(snapshot, legalActions, result => hint = result);
        }

        SimulatedAction llmAction = TrySelectLLMAction(hint, legalActions);
        if (useMLAgents && llmAction == null)
        {
            yield return RequestMLAgentsDecision();
            ScheduleNextAction();
            actionInProgress = false;
            yield break;
        }

        SimulatedAction selectedAction = llmAction;
        if (selectedAction == null)
        {
            MCTSResult result = GetPlanner().Search(snapshot);
            if (enableAIDebugLog)
            {
                Debug.Log(result.GetDebugSummary(snapshot), this);
            }

            selectedAction = result.SelectedAction;
            if (llmAction == null && hint != null && enableAIDebugLog && !training)
            {
                Debug.Log($"[AI Hybrid] LLM hint rejected (confidence={hint.confidence:0.00} < {llmConfidenceThreshold:0.00}), keeping local planner result.", this);
            }
        }
        else if (enableAIDebugLog)
        {
            Debug.Log($"[AI Hybrid] LLM={hint.strategy}, confidence={hint.confidence:0.00}, action={LLMActionPromptBuilder.FormatAction(snapshot, selectedAction)}", this);
        }

        if (selectedAction == null || !ExecuteAction(selectedAction))
        {
            Debug.LogWarning($"[AI MCTS] Selected action could not be executed: {LLMActionPromptBuilder.FormatAction(snapshot, selectedAction)}", this);
        }
        ScheduleNextAction();
        actionInProgress = false;
    }

    private SimulatedAction TrySelectLLMAction(LLMStrategyHint hint, List<SimulatedAction> legalActions)
    {
        if (hint == null
            || legalActions == null
            || hint.confidence < llmConfidenceThreshold
            || hint.action_index < 0
            || hint.action_index >= legalActions.Count)
        {
            return null;
        }

        return legalActions[hint.action_index];
    }

    private IEnumerator RequestMLAgentsDecision()
    {
        CardBattleAgent agent = ResolveMLAgentsAgent();
        if (agent == null || !agent.enabled)
        {
            yield break;
        }

        agent.BeginTurnDecision();
        agent.RequestDecision();
        float deadline = Time.unscaledTime + 2f;
        while (!agent.HasFinishedTurnDecision && Time.unscaledTime < deadline)
        {
            yield return null;
        }
    }

    private bool ShouldUseMLAgentsPolicy()
    {
        if (controlMode != AIControlMode.MLAgents)
        {
            return false;
        }

        CardBattleAgent agent = ResolveMLAgentsAgent();
        if (agent == null)
        {
            WarnMLAgentsFallback("CardBattleAgent is missing.");
            return false;
        }

        if (IsMLAgentsTraining() || agent.HasAssignedModel())
        {
            return true;
        }

        WarnMLAgentsFallback("no ONNX model is assigned and mlagents-learn is not connected.");
        return false;
    }

    private bool IsMLAgentsTraining()
    {
        return Academy.IsInitialized && Academy.Instance.IsCommunicatorOn;
    }

    private void WarnMLAgentsFallback(string reason)
    {
        if (loggedMLAgentsFallback)
        {
            return;
        }

        loggedMLAgentsFallback = true;
        Debug.LogWarning($"[AI Hybrid] ML-Agents mode unavailable ({reason}) Falling back to local planner.", this);
    }

    private void SyncMLAgentsComponents()
    {
        CardBattleAgent agent = ResolveMLAgentsAgent();
        DecisionRequester requester = GetComponent<DecisionRequester>();
        if (requester != null && requester.enabled)
        {
            requester.enabled = false;
        }

        if (agent == null)
        {
            return;
        }

        agent.BindController(this);
        bool enableAgent = controlMode == AIControlMode.MLAgents;
        if (agent.enabled != enableAgent)
        {
            agent.enabled = enableAgent;
        }
    }

    private CardBattleAgent ResolveMLAgentsAgent()
    {
        if (mlAgentsAgent == null)
        {
            mlAgentsAgent = GetComponent<CardBattleAgent>();
        }

        return mlAgentsAgent;
    }

    private bool ShouldRequestLLMHint(BattleStateSnapshot snapshot, List<SimulatedAction> legalActions)
    {
        if (snapshot == null || legalActions == null || legalActions.Count == 0)
        {
            return false;
        }

        PlayerStateSnapshot me = snapshot.GetPlayer(snapshot.CurrentPlayerIndex);
        PlayerStateSnapshot enemy = FindOpponentForLLM(snapshot, snapshot.CurrentPlayerIndex);
        if (me == null || enemy == null)
        {
            return false;
        }

        if (completedAiTurns <= 0)
        {
            return true;
        }

        if (completedAiTurns % llmStrategyIntervalTurns == 0)
        {
            return true;
        }

        return me.Health < enemy.Health * llmDisadvantageHealthRatio;
    }

    private static PlayerStateSnapshot FindOpponentForLLM(BattleStateSnapshot snapshot, int playerIndex)
    {
        foreach (PlayerStateSnapshot player in snapshot.Players)
        {
            if (player != null && player.PlayerIndex != playerIndex)
            {
                return player;
            }
        }

        return null;
    }

    public override void StartTurn()
    {
        base.StartTurn();
        ScheduleNextAction();
    }

    public override void EndTurn()
    {
        base.EndTurn();
        nextActionTime = 0f;
        completedAiTurns++;
    }

    private bool ShouldProcessTurnActions()
    {
        return !isMainPlayer
            && isInTurn
            && GM.Ins != null
            && GM.Ins.BM != null
            && GM.Ins.BM.CurrentPlayer == this
            && !GM.Ins.BM.IsGameOver
            && !GM.Ins.BM.IsTurnTransitioning
            && (GM.Ins.BM.EM == null || !GM.Ins.BM.EM.IsProcessingEffects)
            && (GM.Ins.BM.TM == null || !GM.Ins.BM.TM.HasActiveSelection);
    }

    private void ScheduleNextAction()
    {
        nextActionTime = Time.time + actionInterval;
    }

    private IAIPlanner GetPlanner()
    {
        if (planner == null)
        {
            planner = AIPlannerFactory.Create(modelConfig, new MCTSSettings
            {
                Iterations = searchIterations,
                TimeBudgetMs = searchTimeBudgetMs,
                ExplorationConstant = explorationConstant,
                RolloutActionLimit = rolloutActionLimit,
                ExpandTopCandidatesBias = 3,
                MaxRootTurns = maxRootTurns,
            });
        }
        return planner;
    }

    private void DisposePlanner()
    {
        if (planner is System.IDisposable disposable)
        {
            disposable.Dispose();
        }
        planner = null;
    }

    private bool ExecuteAction(SimulatedAction action)
    {
        switch (action.Type)
        {
            case SimulatedActionType.PlayHandCard:
                return ExecutePlayHandCard(action);
            case SimulatedActionType.UseFieldCast:
                CardController caster = FindRuntimeCard(action.SourceCardId);
                return caster != null && GM.Ins.BM.TryUseFieldCast(caster, true, ResolveRuntimeTargets(action.Targets));
            case SimulatedActionType.AttackMinion:
                CardController attacker = FindRuntimeCard(action.SourceCardId);
                CardController minionTarget = action.Targets.Count > 0 ? FindRuntimeCard(action.Targets[0].Id) : null;
                if (attacker == null || minionTarget == null || !GM.Ins.BM.CanResolveMinionAttack(attacker, minionTarget)) return false;
                GM.Ins.BM.ResolveMinionAttack(attacker, minionTarget);
                return true;
            case SimulatedActionType.AttackPlayer:
                CardController playerAttacker = FindRuntimeCard(action.SourceCardId);
                PlayerController playerTarget = action.Targets.Count > 0 ? FindRuntimePlayer(action.Targets[0].Id) : null;
                if (playerAttacker == null || playerTarget == null || !GM.Ins.BM.CanResolvePlayerAttack(playerAttacker, playerTarget)) return false;
                GM.Ins.BM.ResolvePlayerAttack(playerAttacker, playerTarget);
                return true;
            case SimulatedActionType.EndTurn:
                if (GM.Ins.BM.CurrentPlayer != this) return false;
                GM.Ins.BM.EndCurrentTurn();
                return true;
            default:
                return false;
        }
    }

    public bool TryExecutePlannedAction(SimulatedAction action)
    {
        return action != null && ExecuteAction(action);
    }

    private LLMStrategyClient ResolveLLMClient()
    {
        if (llmStrategyClient == null)
        {
            llmStrategyClient = GetComponent<LLMStrategyClient>();
        }
        if (llmStrategyClient == null)
        {
            llmStrategyClient = gameObject.AddComponent<LLMStrategyClient>();
        }
        return llmStrategyClient;
    }

    private bool ExecutePlayHandCard(SimulatedAction action)
    {
        CardController card = FindRuntimeCard(action.SourceCardId);
        if (card == null || card.cardData == null || card.player != this || card.state != CardState.Hand)
        {
            return false;
        }
        List<UnityEngine.Object> targets = ResolveRuntimeTargets(action.Targets);
        FieldController targetField = card.cardData.cardType == CardType.Minion ? fieldController : null;
        return GM.Ins.BM.TryQueueHandCardPlay(card, targetField, targets);
    }

    private CardController FindRuntimeCard(int runtimeId)
    {
        if (GM.Ins == null || GM.Ins.BM == null) return null;
        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null) continue;
            CardController card = FindCard(player.handController != null ? player.handController.handCards : null, runtimeId)
                ?? FindCard(player.fieldController != null ? player.fieldController.fieldCards : null, runtimeId)
                ?? FindCard(player.graveCards, runtimeId)
                ?? FindCard(player.deckCards, runtimeId);
            if (card != null) return card;
        }
        return null;
    }

    private PlayerController FindRuntimePlayer(int playerIndex)
    {
        return GM.Ins != null && GM.Ins.BM != null && playerIndex >= 0 && playerIndex < GM.Ins.BM.players.Count
            ? GM.Ins.BM.players[playerIndex]
            : null;
    }

    private List<UnityEngine.Object> ResolveRuntimeTargets(List<SimulatedTarget> targets)
    {
        List<UnityEngine.Object> result = new();
        foreach (SimulatedTarget target in targets)
        {
            UnityEngine.Object runtimeTarget = target.Kind == SimulatedTargetKind.Player
                ? FindRuntimePlayer(target.Id)
                : FindRuntimeCard(target.Id);
            if (runtimeTarget != null && !result.Contains(runtimeTarget)) result.Add(runtimeTarget);
        }
        return result;
    }

    private static CardController FindCard(List<CardController> cards, int runtimeId)
    {
        if (cards == null) return null;
        foreach (CardController card in cards) if (card != null && card.GetInstanceID() == runtimeId) return card;
        return null;
    }
}
