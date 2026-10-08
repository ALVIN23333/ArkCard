using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(BehaviorParameters), typeof(DecisionRequester))]
public sealed class CardBattleAgent : Agent
{
    [SerializeField, Range(8, 128)] private int maxActionBranchSize = 64;
    [SerializeField] private AIController controller;

    private const float StepPenalty = -0.001f;
    private const float PlayCardReward = 0.05f;
    private const float UseSkillReward = 0.05f;
    private const float HeroDamageScale = 0.1f;
    private const float HeroDamageTakenScale = -0.1f;
    private const float KillEnemyMinionReward = 0.2f;
    private const float LoseAllyMinionReward = -0.15f;
    private const float DamagingTurnEndReward = 0.2f;
    private const float WinReward = 1.0f;
    private const float LoseReward = -1.0f;
    private const float InvalidActionReward = -0.02f;

    private readonly BattleStateSimulator simulator = new();
    private float previousHealth;
    private bool turnDecisionPending;
    private bool turnDecisionCompleted;
    private bool lastTurnDecisionExecuted;

    public bool HasFinishedTurnDecision => turnDecisionCompleted;
    public bool LastTurnDecisionExecuted => lastTurnDecisionExecuted;

    public override void Initialize()
    {
        BindController(controller);
        previousHealth = GetHealth();
    }

    public override void OnEpisodeBegin()
    {
        previousHealth = GetHealth();
        turnDecisionPending = false;
        turnDecisionCompleted = false;
        lastTurnDecisionExecuted = false;
    }

    public void BindController(AIController value)
    {
        if (value != null)
        {
            controller = value;
        }
        else if (controller == null)
        {
            controller = GetComponent<AIController>();
        }
    }

    public void BeginTurnDecision()
    {
        turnDecisionPending = true;
        turnDecisionCompleted = false;
        lastTurnDecisionExecuted = false;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        BattleStateSnapshot state = null;
        int playerIndex = 0;
        if (controller != null && GM.Ins != null && GM.Ins.BM != null)
        {
            playerIndex = GM.Ins.BM.players.IndexOf(controller);
            SnapshotFactory.TryCreate(GM.Ins.BM, playerIndex, out state, out _, null);
        }

        sensor.AddObservation(CardBattleObservationBuilder.Build(state, playerIndex));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        bool executed = false;
        try
        {
            if (controller == null || !controller.IsMLAgentsMode)
            {
                return;
            }

            if (GM.Ins == null || GM.Ins.BM == null)
            {
                return;
            }

            if (GM.Ins.BM.IsGameOver)
            {
                EndEpisode();
                return;
            }

            if (!controller.isInTurn
                || GM.Ins.BM.IsTurnTransitioning
                || (GM.Ins.BM.EM != null && GM.Ins.BM.EM.IsProcessingEffects)
                || (GM.Ins.BM.TM != null && GM.Ins.BM.TM.HasActiveSelection))
            {
                return;
            }

            int playerIndex = GM.Ins.BM.players.IndexOf(controller);
            if (!SnapshotFactory.TryCreate(GM.Ins.BM, playerIndex, out BattleStateSnapshot state, out _, null))
            {
                AddReward(InvalidActionReward);
                return;
            }

            if (state.IsGameOver)
            {
                EndEpisode();
                return;
            }

            List<SimulatedAction> legalActions = simulator.GenerateLegalActions(state);
            int actionIndex = actions.DiscreteActions[0];
            if (actionIndex < 0 || actionIndex >= legalActions.Count || actionIndex >= maxActionBranchSize)
            {
                AddReward(InvalidActionReward);
                return;
            }

            SimulatedAction action = legalActions[actionIndex];
            BattleStateSnapshot before = state;
            if (!controller.TryExecutePlannedAction(action))
            {
                AddReward(InvalidActionReward);
                return;
            }

            executed = true;

            BattleStateSnapshot after = null;
            if (SnapshotFactory.TryCreate(GM.Ins.BM, playerIndex, out after, out _, null))
            {
                AddReward(ComputeActionReward(before, after, action, playerIndex));
            }
            else
            {
                AddReward(StepPenalty);
            }

            previousHealth = GetHealth();
            if (GM.Ins.BM.IsGameOver || (after != null && after.IsGameOver))
            {
                EndEpisode();
            }
        }
        finally
        {
            if (turnDecisionPending)
            {
                turnDecisionPending = false;
                turnDecisionCompleted = true;
                lastTurnDecisionExecuted = executed;
            }
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        actionsOut.DiscreteActions.Array[0] = 0;
    }

    public bool HasAssignedModel()
    {
        BehaviorParameters behavior = GetComponent<BehaviorParameters>();
        return behavior != null
            && behavior.Model != null
            && behavior.BehaviorType != BehaviorType.HeuristicOnly;
    }

    private static float ComputeActionReward(
        BattleStateSnapshot before,
        BattleStateSnapshot after,
        SimulatedAction action,
        int playerIndex)
    {
        float reward = StepPenalty;
        if (before == null || after == null || action == null)
        {
            return reward;
        }

        PlayerStateSnapshot myBefore = before.GetPlayer(playerIndex);
        PlayerStateSnapshot myAfter = after.GetPlayer(playerIndex);
        PlayerStateSnapshot enemyBefore = FindOpponent(before, playerIndex);
        PlayerStateSnapshot enemyAfter = FindOpponent(after, playerIndex);
        if (myBefore == null || myAfter == null || enemyBefore == null || enemyAfter == null)
        {
            return reward;
        }

        switch (action.Type)
        {
            case SimulatedActionType.PlayHandCard:
                reward += PlayCardReward;
                break;
            case SimulatedActionType.UseFieldCast:
                reward += UseSkillReward;
                break;
        }

        int enemyHeroDamage = Mathf.Max(0, enemyBefore.Health - enemyAfter.Health);
        int allyHeroDamage = Mathf.Max(0, myBefore.Health - myAfter.Health);
        reward += HeroDamageScale * enemyHeroDamage;
        reward += HeroDamageTakenScale * allyHeroDamage;

        int enemyMinionsLost = enemyBefore.Field.Count - enemyAfter.Field.Count;
        int allyMinionsLost = myBefore.Field.Count - myAfter.Field.Count;
        if (enemyMinionsLost > 0)
        {
            reward += KillEnemyMinionReward * enemyMinionsLost;
        }
        if (allyMinionsLost > 0)
        {
            reward += LoseAllyMinionReward * allyMinionsLost;
        }

        if (action.Type == SimulatedActionType.EndTurn && enemyHeroDamage > 0)
        {
            reward += DamagingTurnEndReward;
        }

        if (after.IsGameOver)
        {
            bool enemyDead = enemyAfter.Health <= 0;
            bool allyDead = myAfter.Health <= 0;
            if (enemyDead && !allyDead)
            {
                reward += WinReward;
            }
            else if (allyDead && !enemyDead)
            {
                reward += LoseReward;
            }
        }

        return reward;
    }

    private static PlayerStateSnapshot FindOpponent(BattleStateSnapshot state, int playerIndex)
    {
        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player != null && player.PlayerIndex != playerIndex)
            {
                return player;
            }
        }

        return null;
    }

    private float GetHealth()
    {
        return controller != null ? controller.health : 0f;
    }
}
