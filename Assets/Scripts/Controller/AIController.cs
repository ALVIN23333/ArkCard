// ============================================================================
// 【改动 2 / LLM 调用时机 + confidence 回退，对齐《AI集成方案设计文档》3.1、3.3 节】
//
// 本文件 = Assets/Scripts/Controller/AIController.cs 的修改副本。
// 原文件未做任何改动，本副本只放在 new/ 目录下供你审阅后手工搬回。
//
// 与原文件的差异（共 4 处）：
//   [1] 新增 "LLM Call Policy" 配置区：调用间隔 / 置信度阈值 / 劣势比例
//   [2] ProcessTurnAction 中先判断是否到调用时机，再决定要不要请求 LLM
//       （对应文档 3.1：对局开始时、每 3 回合、AI 处于劣势时）
//   [3] 采纳 LLM 建议时增加 confidence >= 阈值 判断
//       （对应文档 3.3：confidence 低于 0.6 时回退到 ML-Agents / 本地规划器）
//   [4] 新增 ShouldRequestLLMHint / FindOpponentForLLM 两个辅助方法
//   [5] 修正调用限频：改用本地 completedAiTurns 计数
//
// 关于 [5]：最初设想用快照里的 RootEndTurnCount 做“每 N 回合 / 开局”判断，
// 但该字段只在 BattleStateSimulator 的内部副本上递增，SnapshotFactory 构造
// 正式对局快照时从不设置它（恒为 0），会使“每 N 回合”永不生效、每次决策都调用。
// 因此改为在 EndTurn 里累加本地计数器 completedAiTurns。
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    [Header("Hybrid AI")]
    [SerializeField]
    private bool enableLLMStrategy;
    [SerializeField]
    private LLMStrategyClient llmStrategyClient;

    // [1] LLM 调用策略：集中暴露，便于在 Inspector 里按文档 3.1 调参
    [Header("LLM Call Policy")]
    [SerializeField, Min(1)]
    private int llmStrategyIntervalTurns = 3;          // 每 N 回合询问一次策略
    [SerializeField, Range(0f, 1f)]
    private float llmConfidenceThreshold = 0.6f;       // 低于该置信度则不采纳 LLM 建议
    [SerializeField, Range(0f, 1f)]
    private float llmDisadvantageHealthRatio = 0.5f;   // 己方血量低于敌方该比例时视为劣势

    private float nextActionTime;
    private IAIPlanner planner;
    private bool actionInProgress;

    // 【修正】本地已完成的 AI 回合计数。
    // 原方案用 snapshot.RootEndTurnCount 判断调用时机，但该字段只在
    // BattleStateSimulator 内部副本上递增，SnapshotFactory 构造正式对局快照时
    // 从不设置它（恒为 0），导致 ShouldRequestLLMHint 的第一个分支每次都命中，
    // 「每 N 回合」形同虚设。改用这个本地计数器才能真正限频。
    private int completedAiTurns;

    private void OnEnable()
    {
        IsAIControlled = true;
        actionInProgress = false;
    }

    private void OnDisable()
    {
        IsAIControlled = false;
        DisposePlanner();
    }

    private void Update()
    {
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

        // [2] 先看是否处于文档 3.1 约定的调用时机，再决定要不要发请求
        LLMStrategyHint hint = null;
        List<SimulatedAction> legalActions = new BattleStateSimulator().GenerateLegalActions(snapshot);
        if (enableLLMStrategy
            && ResolveLLMClient() != null
            && ShouldRequestLLMHint(snapshot, legalActions))
        {
            yield return llmStrategyClient.RequestHint(snapshot, legalActions, result => hint = result);
        }

        MCTSResult result = GetPlanner().Search(snapshot);
        if (enableAIDebugLog)
        {
            Debug.Log(result.GetDebugSummary(snapshot), this);
        }

        SimulatedAction selectedAction = result.SelectedAction;

        // [3] 只有同时满足“置信度达标 + 下标合法”才采纳 LLM 建议
        if (hint != null
            && hint.confidence >= llmConfidenceThreshold
            && hint.action_index >= 0
            && hint.action_index < legalActions.Count)
        {
            selectedAction = legalActions[hint.action_index];
            if (enableAIDebugLog)
            {
                Debug.Log($"[AI Hybrid] LLM={hint.strategy}, confidence={hint.confidence:0.00}, action={selectedAction}", this);
            }
        }
        else if (hint != null && enableAIDebugLog)
        {
            Debug.Log($"[AI Hybrid] LLM hint rejected (confidence={hint.confidence:0.00} < {llmConfidenceThreshold:0.00}), keeping local planner result.", this);
        }

        if (selectedAction == null || !ExecuteAction(selectedAction))
        {
            Debug.LogWarning($"[AI MCTS] Selected action could not be executed: {selectedAction}", this);
        }
        ScheduleNextAction();
        actionInProgress = false;
    }

    /// <summary>
    /// 对应《AI集成方案设计文档》3.1 节的调用时机：
    /// 开局、每 llmStrategyIntervalTurns 回合、以及 AI 处于劣势时。
    /// </summary>
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

        // 【修正】原先这里判断 snapshot.RootEndTurnCount，但该字段在正式对局快照中恒为 0
        // （SnapshotFactory 不设置它），导致第一个分支每次都命中、「每 N 回合」不生效。
        // 改用本地 completedAiTurns：0 表示开局（调用一次），之后每 N 回合调用一次。
        if (completedAiTurns <= 0)
        {
            return true;   // 对局开局
        }

        if (completedAiTurns % llmStrategyIntervalTurns == 0)
        {
            return true;   // 每 N 个 AI 回合
        }

        // AI 处于劣势（文档表述为“己方血量低于敌方 50%”）
        return me.Health < enemy.Health * llmDisadvantageHealthRatio;
    }

    // [4] 与 CardBattleAgent.FindOpponent 同构：遍历 Players 取另一个玩家
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
        completedAiTurns++;   // 【修正】供 ShouldRequestLLMHint 做调用限频
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
