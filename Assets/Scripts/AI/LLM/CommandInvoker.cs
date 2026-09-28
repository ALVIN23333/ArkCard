// ============================================================================
// 【改动 4-2 / 命令模式，对齐《AI集成方案设计文档》第 4 章】
//
// 文档第 4 章列出：
//   · CommandInvoker.cs  # 命令模式，支持撤销
//   · AICommandMapper.cs # 将 LLM/ML-Agents 输出映射为 Command
//
// 本文件提供 CommandInvoker：一个保存命令历史的执行器，供 AICommandMapper
// 包装出来的命令对象登记与回滚。
//
// 与项目实际实现的关系（重要，不要误解）：
//   项目里 AI 动作的真实执行入口是 AIController.ExecuteAction + BattleManager，
//   真正的“选目标阶段回滚”在 TargetManager。本类只提供文档要求的命令模式
//   外壳，用于日志/调试/后续扩展，不替换上述任何现有链路。
// ============================================================================

using System.Collections.Generic;

public sealed class CommandInvoker
{
    private readonly List<IAICommand> history = new();

    /// <summary>已执行的命令数量。</summary>
    public int Count => history.Count;

    /// <summary>执行命令；成功则入栈，供后续撤销。</summary>
    public bool Execute(IAICommand command)
    {
        if (command == null)
        {
            return false;
        }

        if (!command.Execute())
        {
            return false;
        }

        history.Add(command);
        return true;
    }

    /// <summary>撤销最近一条可撤销命令；没有可撤销命令时返回 false。</summary>
    public bool UndoLast()
    {
        for (int index = history.Count - 1; index >= 0; index--)
        {
            IAICommand command = history[index];
            if (command == null || !command.CanUndo)
            {
                continue;
            }

            if (command.Undo())
            {
                history.RemoveAt(index);
                return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>清空命令历史（例如回合结束或对局重开时调用）。</summary>
    public void Clear()
    {
        history.Clear();
    }

    /// <summary>导出命令历史摘要，便于写 AI 日志。</summary>
    public string DescribeHistory()
    {
        if (history.Count == 0)
        {
            return "(empty)";
        }

        List<string> parts = new(history.Count);
        foreach (IAICommand command in history)
        {
            parts.Add(command != null ? command.Description : "(null)");
        }

        return string.Join(" -> ", parts);
    }
}
