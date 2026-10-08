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

    // 文档 2.4 节奖励权重：集中成常量，方便和设计文档逐条对照
    private const float StepPenalty = -0.001f;          // 每步微小负奖励
    private const float PlayCardReward = 0.05f;         // 成功打出一张卡牌
    private const float UseSkillReward = 0.05f;         // 成功释放随从技能
    private const float HeroDamageScale = 0.1f;         // 对敌方英雄造成伤害 +0.1 × 伤害值
    private const float HeroDamageTakenScale = -0.1f;   // 己方英雄受到伤害 -0.1 × 伤害值
    private const float KillEnemyMinionReward = 0.2f;   // 消灭敌方随从
    private const float LoseAllyMinionReward = -0.15f;  // 己方随从死亡
    private const float DamagingTurnEndReward = 0.2f;   // 回合结束且本回合对敌方造成过伤害
    private const float WinReward = 1.0f;               // 获胜
    private const float LoseReward = -1.0f;             // 失败
    private const float InvalidActionReward = -0.02f;   // 非法动作惩罚（文档 2.3 节）

    private readonly BattleStateSimulator simulator = new();
    private float previousHealth;

    public override void Initialize()
    {
        if (controller == null)
        {
            controller = GetComponent<AIController>();
        }
        previousHealth = GetHealth();
    }

    public override void OnEpisodeBegin()
    {
        previousHealth = GetHealth();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (controller == null || GM.Ins == null || GM.Ins.BM == null)
        {
            sensor.AddObservation(Vector3.zero);
            return;
        }

        int playerIndex = GM.Ins.BM.players.IndexOf(controller);
        if (!SnapshotFactory.TryCreate(GM.Ins.BM, playerIndex, out BattleStateSnapshot state, out _, null))
        {
            sensor.AddObservation(Vector3.zero);
            return;
        }

        sensor.AddObservation(AIFeatureEncoder.EncodeState(state, playerIndex));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // 正式对局由 AIController 执行。没有连接 mlagents-learn 时不得出牌，
        // 否则 Heuristic 会选合法动作 0，和 MCTS/LLM 抢步。
        if (!Academy.Instance.IsCommunicatorOn)
        {
            return;
        }

        if (controller == null || GM.Ins == null || GM.Ins.BM == null || !controller.isInTurn)
        {
            return;
        }

        int playerIndex = GM.Ins.BM.players.IndexOf(controller);
        if (!SnapshotFactory.TryCreate(GM.Ins.BM, playerIndex, out BattleStateSnapshot state, out _, null))
        {
            AddReward(InvalidActionReward);
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

        if (SnapshotFactory.TryCreate(GM.Ins.BM, playerIndex, out BattleStateSnapshot after, out _, null))
        {
            AddReward(ComputeActionReward(before, after, action, playerIndex));
        }
        else
        {
            AddReward(StepPenalty);
        }

        previousHealth = GetHealth();
        if (GM.Ins.BM.IsGameOver)
        {
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        actionsOut.DiscreteActions.Array[0] = 0;
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
            if (enemyDead && allyDead)
            {
                reward += 0f;
            }
            else if (enemyDead)
            {
                reward += WinReward;
            }
            else if (allyDead)
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
