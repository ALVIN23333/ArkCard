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

    private float nextActionTime;
    private MCTSPlanner planner;

    private void OnEnable()
    {
        IsAIControlled = true;
    }

    private void Update()
    {
        if (!ShouldProcessTurnActions() || Time.time < nextActionTime)
        {
            return;
        }

        BattleManager battleManager = GM.Ins.BM;
        if (!SnapshotFactory.TryCreate(battleManager, GM.Ins.BM.players.IndexOf(this), out BattleStateSnapshot snapshot, out string error, opponentBeliefPool))
        {
            Debug.LogWarning($"[AI MCTS] Snapshot creation failed: {error}", this);
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
            Debug.LogWarning($"[AI MCTS] Selected action could not be executed: {result.SelectedAction}", this);
        }
        ScheduleNextAction();
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

    private MCTSPlanner GetPlanner()
    {
        if (planner == null)
        {
            planner = new MCTSPlanner(new MCTSSettings
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

    private bool ExecutePlayHandCard(SimulatedAction action)
    {
        CardController card = FindRuntimeCard(action.SourceCardId);
        if (card == null || card.cardData == null || card.player != this || card.state != CardState.Hand)
        {
            return false;
        }
        List<UnityEngine.Object> targets = ResolveRuntimeTargets(action.Targets);
        if (card.cardData.cardType == CardType.SPELL)
        {
            if (!GM.Ins.BM.CanUseSpell(card) || !SpendCost(card.cost)) return false;
            TargetManager targetManager = GM.Ins.BM.TM;
            void TriggerSpellAfterHangingReady()
            {
                GM.Ins.BM.EM.TriggerSpellEffect(card, targets, () =>
                {
                    if (targetManager != null) targetManager.ReleaseHangingState(card, () => SendCardToGraveyard(card));
                    else SendCardToGraveyard(card);
                });
            }
            if (targetManager == null || !targetManager.EnterHangingState(card, false, TriggerSpellAfterHangingReady))
            {
                TriggerSpellAfterHangingReady();
            }
            return true;
        }
        if (card.cardData.cardType != CardType.Minion || !GM.Ins.BM.CanUseMinion(card, fieldController) || !SpendCost(card.cost))
        {
            return false;
        }
        card.transform.localScale = Vector3.one;
        handController.RemoveCard(card);
        fieldController.AddCard(card);
        AnimeManager.Delay(AnimeManager.FieldRefreshDuration, () =>
        {
            if (card != null && card.state == CardState.Field)
            {
                GM.Ins.BM.EM.TriggerCardEffect(card, TriggerType.Enter, targets);
            }
        });
        return true;
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
