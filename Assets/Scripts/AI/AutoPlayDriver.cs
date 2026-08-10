using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 调试/测试辅助：让主玩家像 AI 一样自动执行 MCTS 规划出的动作。
/// 与 AIController 相互独立，默认关闭，由 GM 面板或 PlayMode 测试开启。
/// </summary>
public class AutoPlayDriver : MonoBehaviour
{
    [SerializeField]
    private float actionInterval = 1.5f;
    [SerializeField]
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

    private float nextActionTime;
    private IAIPlanner planner;

    public static AutoPlayDriver GetOrCreate()
    {
        AutoPlayDriver existing = FindObjectOfType<AutoPlayDriver>();
        if (existing != null)
        {
            return existing;
        }

        GameObject driverObject = new GameObject("AutoPlayDriver");
        AutoPlayDriver driver = driverObject.AddComponent<AutoPlayDriver>();
        DontDestroyOnLoad(driverObject);
        return driver;
    }

    private void Update()
    {
        if (enabled && GM.Ins != null && GM.Ins.BM != null)
        {
            SetMainPlayerAIControl(true);
        }

        if (!enabled || Time.time < nextActionTime || !ShouldProcessTurnActions())
        {
            return;
        }

        BattleManager battleManager = GM.Ins.BM;
        PlayerController current = battleManager.CurrentPlayer;
        int aiPlayerIndex = current != null ? battleManager.players.IndexOf(current) : -1;
        if (!SnapshotFactory.TryCreate(battleManager, aiPlayerIndex, out BattleStateSnapshot snapshot, out string error, opponentBeliefPool))
        {
            Debug.LogWarning($"[AutoPlayDriver] Snapshot creation failed: {error}", this);
            ScheduleNextAction();
            return;
        }

        MCTSResult result = GetPlanner().Search(snapshot);
        if (enableAIDebugLog)
        {
            Debug.Log(result.GetDebugSummary(snapshot), this);
        }

        if (result.SelectedAction == null || !ExecuteAction(result.SelectedAction))
        {
            Debug.LogWarning($"[AutoPlayDriver] Selected action could not be executed: {result.SelectedAction}", this);
        }
        ScheduleNextAction();
    }

    private bool ShouldProcessTurnActions()
    {
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            return false;
        }

        BattleManager battleManager = GM.Ins.BM;
        PlayerController current = battleManager.CurrentPlayer;
        return current != null
            && current.isMainPlayer
            && current.isInTurn
            && !battleManager.IsGameOver
            && !battleManager.IsTurnTransitioning
            && (battleManager.EM == null || !battleManager.EM.IsProcessingEffects)
            && (battleManager.TM == null || !battleManager.TM.HasActiveSelection);
    }

    private void ScheduleNextAction()
    {
        nextActionTime = Time.time + actionInterval;
    }

    private void OnDisable()
    {
        SetMainPlayerAIControl(false);
        DisposePlanner();
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

    private static void SetMainPlayerAIControl(bool value)
    {
        if (GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.players == null)
        {
            return;
        }
        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player != null && player.isMainPlayer)
            {
                player.IsAIControlled = value;
            }
        }
    }

    private bool ExecuteAction(SimulatedAction action)
    {
        switch (action.Type)
        {
            case SimulatedActionType.PlayHandCard:
                return ExecutePlayHandCard(action);
            case SimulatedActionType.UseFieldCast:
            {
                CardController caster = FindRuntimeCard(action.SourceCardId);
                return caster != null && GM.Ins.BM.TryUseFieldCast(caster, true, ResolveRuntimeTargets(action.Targets));
            }
            case SimulatedActionType.AttackMinion:
            {
                CardController attacker = FindRuntimeCard(action.SourceCardId);
                CardController minionTarget = action.Targets.Count > 0 ? FindRuntimeCard(action.Targets[0].Id) : null;
                if (attacker == null || minionTarget == null || !GM.Ins.BM.CanResolveMinionAttack(attacker, minionTarget))
                {
                    return false;
                }
                GM.Ins.BM.ResolveMinionAttack(attacker, minionTarget);
                return true;
            }
            case SimulatedActionType.AttackPlayer:
            {
                CardController attacker = FindRuntimeCard(action.SourceCardId);
                PlayerController playerTarget = action.Targets.Count > 0 ? FindRuntimePlayer(action.Targets[0].Id) : null;
                if (attacker == null || playerTarget == null || !GM.Ins.BM.CanResolvePlayerAttack(attacker, playerTarget))
                {
                    return false;
                }
                GM.Ins.BM.ResolvePlayerAttack(attacker, playerTarget);
                return true;
            }
            case SimulatedActionType.EndTurn:
                if (GM.Ins.BM.CurrentPlayer == null || !GM.Ins.BM.CurrentPlayer.isMainPlayer)
                {
                    return false;
                }
                GM.Ins.BM.EndMainPlayerTurn();
                return true;
            default:
                return false;
        }
    }

    private bool ExecutePlayHandCard(SimulatedAction action)
    {
        CardController card = FindRuntimeCard(action.SourceCardId);
        if (card == null || card.cardData == null || card.player == null || card.state != CardState.Hand)
        {
            return false;
        }

        List<UnityEngine.Object> targets = ResolveRuntimeTargets(action.Targets);
        FieldController targetField = card.cardData.cardType == CardType.Minion
            ? card.player.fieldController
            : null;
        return GM.Ins.BM.TryQueueHandCardPlay(card, targetField, targets);
    }

    private CardController FindRuntimeCard(int runtimeId)
    {
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            return null;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null)
            {
                continue;
            }

            CardController card = FindCard(player.handController != null ? player.handController.handCards : null, runtimeId)
                ?? FindCard(player.fieldController != null ? player.fieldController.fieldCards : null, runtimeId)
                ?? FindCard(player.graveCards, runtimeId)
                ?? FindCard(player.deckCards, runtimeId);
            if (card != null)
            {
                return card;
            }
        }
        return null;
    }

    private PlayerController FindRuntimePlayer(int playerIndex)
    {
        if (GM.Ins == null || GM.Ins.BM == null || playerIndex < 0 || playerIndex >= GM.Ins.BM.players.Count)
        {
            return null;
        }
        return GM.Ins.BM.players[playerIndex];
    }

    private List<UnityEngine.Object> ResolveRuntimeTargets(List<SimulatedTarget> targets)
    {
        List<UnityEngine.Object> result = new();
        foreach (SimulatedTarget target in targets)
        {
            UnityEngine.Object runtimeTarget = target.Kind == SimulatedTargetKind.Player
                ? FindRuntimePlayer(target.Id)
                : FindRuntimeCard(target.Id);
            if (runtimeTarget != null && !result.Contains(runtimeTarget))
            {
                result.Add(runtimeTarget);
            }
        }
        return result;
    }

    private static CardController FindCard(List<CardController> cards, int runtimeId)
    {
        if (cards == null)
        {
            return null;
        }
        foreach (CardController card in cards)
        {
            if (card != null && card.GetInstanceID() == runtimeId)
            {
                return card;
            }
        }
        return null;
    }
}
