// ============================================================================
// 【改动 1 / 奖励函数对齐《AI集成方案设计文档》2.4 节奖励表】
//
// 本文件 = Assets/Scripts/AI/ML/Agents/CardBattleAgent.cs 的修改副本。
// 原文件未做任何改动，本副本只放在 new/ 目录下供你审阅后手工搬回。
//
// 与原文件的差异（共 3 处）：
//   [1] OnActionReceived 执行动作前后各取一次快照，用快照差计算奖励
//   [2] 新增 ComputeActionReward / FindOpponent，逐条实现文档 2.4 的奖励表
//   [3] 删除原有的 healthDelta 单行奖励，避免与 [2] 重复计分
//
// 注意：SnapshotFactory.TryCreate 的签名是
//   TryCreate(BattleManager, int aiPlayerIndex, out BattleStateSnapshot, out string, CardListSO)
// ============================================================================

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
        BattleStateSnapshot before = state;   // [1] 执行前快照
        if (!controller.TryExecutePlannedAction(action))
        {
            AddReward(InvalidActionReward);
            return;
        }

        // [1] 执行后重新取快照，用两份快照的差还原文档 2.4 的全部奖励事件。
        // 取不到时退回“只给每步惩罚”，保证不会因为快照失败而中断训练。
        if (SnapshotFactory.TryCreate(GM.Ins.BM, playerIndex, out BattleStateSnapshot after, out _, null))
        {
            AddReward(ComputeActionReward(before, after, action, playerIndex));   // [2]
        }
        else
        {
            AddReward(StepPenalty);
        }

        // [3] 原有 healthDelta 奖励已删除：它已被 ComputeActionReward 中的
        //     “对敌方英雄造成伤害 / 己方英雄受到伤害”两项完整覆盖。
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

    /// <summary>
    /// 按《AI集成方案设计文档》2.4 节奖励表，用执行前后的快照差计算本次动作奖励。
    /// </summary>
    private static float ComputeActionReward(
        BattleStateSnapshot before,
        BattleStateSnapshot after,
        SimulatedAction action,
        int playerIndex)
    {
        float reward = StepPenalty;   // 每步微小负奖励，鼓励快速结束回合

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

        // 事件：成功打出一张卡牌 / 成功释放随从技能
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

        // 事件：对敌方英雄造成伤害 / 己方英雄受到伤害
        reward += HeroDamageScale * enemyHeroDamage;
        reward += HeroDamageTakenScale * allyHeroDamage;

        // 事件：消灭敌方随从 / 己方随从死亡（用场面数量差近似死亡数）
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

        // 事件：回合结束且对敌方造成伤害
        if (action.Type == SimulatedActionType.EndTurn && enemyHeroDamage > 0)
        {
            reward += DamagingTurnEndReward;
        }

        // 事件：获胜 / 失败 / 平局
        if (after.IsGameOver)
        {
            bool enemyDead = enemyAfter.Health <= 0;
            bool allyDead = myAfter.Health <= 0;
            if (enemyDead && allyDead)
            {
                reward += 0f;                  // 平局
            }
            else if (enemyDead)
            {
                reward += WinReward;           // 获胜
            }
            else if (allyDead)
            {
                reward += LoseReward;          // 失败
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
