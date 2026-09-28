// ============================================================================
// 【改动 4-3 / AICommandMapper，对齐《AI集成方案设计文档》第 4 章】
//
// 文档第 4 章：AICommandMapper.cs  # 将 LLM/ML-Agents 输出映射为 Command
// 文档 3.4 节第 3 步：“将 actions 转换为游戏内的 ICommand 对象，依次执行”。
//
// 映射规则（与本项目实际数据结构对应）：
//   LLM / ML-Agents 输出           ->  本项目的 SimulatedAction
//   ------------------------------------------------
//   action_index / 离散动作下标     ->  legalActions[action_index]
//   play_card  + card_index        ->  SimulatedActionType.PlayHandCard + SourceCardId
//   attack     + my_minion_index   ->  SimulatedActionType.AttackMinion / AttackPlayer + Targets
//   use_skill                      ->  SimulatedActionType.UseFieldCast
//   end_turn                       ->  SimulatedActionType.EndTurn
//
// 执行委托：本类不直接调用 BattleManager，而是通过构造时注入的
// AIActionExecutor 委托执行，保持“映射层”和“执行层”解耦，
// 也避免与 AIController 现有执行入口重复。
// ============================================================================

using System;

/// <summary>执行一个已校验的模拟动作；返回是否执行成功。</summary>
public delegate bool AIActionExecutor(SimulatedAction action);

/// <summary>撤销一个已执行的模拟动作；返回是否撤销成功。</summary>
public delegate bool AIActionRollback(SimulatedAction action);

/// <summary>
/// 把 AI 决策结果（动作下标或 SimulatedAction）映射成命令对象。
/// </summary>
public sealed class AICommandMapper
{
    private readonly AIActionExecutor execute;
    private readonly AIActionRollback rollback;

    /// <param name="execute">必需：真正执行动作的委托（例如 AIController.TryExecutePlannedAction）。</param>
    /// <param name="rollback">可选：撤销委托；不传则命令标记为不可撤销。</param>
    public AICommandMapper(AIActionExecutor execute, AIActionRollback rollback = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.rollback = rollback;
    }

    /// <summary>
    /// 按合法动作下标映射命令；下标越界或动作非法时返回 null。
    /// </summary>
    public IAICommand Map(int actionIndex, System.Collections.Generic.IReadOnlyList<SimulatedAction> legalActions)
    {
        if (legalActions == null || actionIndex < 0 || actionIndex >= legalActions.Count)
        {
            return null;
        }

        return Map(legalActions[actionIndex]);
    }

    /// <summary>把一个模拟动作映射成命令对象。</summary>
    public IAICommand Map(SimulatedAction action)
    {
        if (action == null)
        {
            return null;
        }

        return new DelegateAICommand(action.ToString(), action, execute, rollback);
    }

    /// <summary>
    /// 依次执行一串动作（文档 3.4 节：“动作按顺序执行”）。
    /// 返回成功执行的动作数量；某一步失败即停止，保证不会带着错误状态继续。
    /// </summary>
    public int ExecuteInOrder(
        System.Collections.Generic.IReadOnlyList<SimulatedAction> actions,
        CommandInvoker invoker = null)
    {
        if (actions == null)
        {
            return 0;
        }

        int executed = 0;
        for (int index = 0; index < actions.Count; index++)
        {
            IAICommand command = Map(actions[index]);
            if (command == null)
            {
                break;
            }

            bool ok = invoker != null ? invoker.Execute(command) : command.Execute();
            if (!ok)
            {
                break;
            }

            executed++;
        }

        return executed;
    }

    /// <summary>命令模式的最小实现：把执行/回滚委托包装成 IAICommand。</summary>
    private sealed class DelegateAICommand : IAICommand
    {
        private readonly SimulatedAction action;
        private readonly AIActionExecutor execute;
        private readonly AIActionRollback rollback;

        public DelegateAICommand(
            string description,
            SimulatedAction action,
            AIActionExecutor execute,
            AIActionRollback rollback)
        {
            Description = description ?? string.Empty;
            this.action = action;
            this.execute = execute;
            this.rollback = rollback;
        }

        public string Description { get; }

        public bool CanUndo => rollback != null;

        public bool Execute()
        {
            return execute != null && execute(action);
        }

        public bool Undo()
        {
            return rollback != null && rollback(action);
        }
    }
}
