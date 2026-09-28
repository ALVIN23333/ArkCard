// ============================================================================
// 【改动 4-1 / 接口预留，对齐《AI集成方案设计文档》第 4 章“关键接口”】
//
// 本文件是新增文件（原项目中没有 IAIStrategy / ICommand 这类接口）。
// 文档第 4 章写的是：
//   · ICommand：包含 Execute() 与 Undo()。
//   · IAIStrategy：包含 Task<AIAction> DecideAsync(GameState state)，
//     便于替换 ML-Agents 或 LLM。
//
// 这里把接口名与签名按文档保留，只把 “GameState” 落到项目里已经存在的
// 不可变快照类型 BattleStateSnapshot 上（它就是文档语义里的 GameState），
// 避免为了一个接口再造一层状态模型。
//
// 搬运说明：把这些 .cs 放到 Assets/Scripts/AI/LLM/（或 AI/Contracts/）下即可，
// 它们都编译进 Assembly-CSharp，不需要额外 asmdef。
// ============================================================================

using System.Threading.Tasks;

/// <summary>
/// AI 决策策略抽象（文档第 4 章 IAIStrategy）。
/// 实现方可以是本地规划器（神经 MCTS / 旧 MCTS），也可以是 LLM 顾问。
/// </summary>
public interface IAIStrategy
{
    /// <summary>策略名称，用于日志与调试。</summary>
    string Name { get; }

    /// <summary>根据当前局面给出一个动作决策。</summary>
    Task<AIAction> DecideAsync(BattleStateSnapshot state);
}

/// <summary>
/// AI 决策结果（文档第 4 章 AIAction）。
/// 携带动作下标、可读的策略说明、置信度，以及可执行/可撤销的命令包装。
/// </summary>
public sealed class AIAction
{
    /// <summary>本地规划器给出的动作下标（在合法动作列表中的位置）。</summary>
    public int ActionIndex = -1;

    /// <summary>宏观战术说明；LLM 顾问会返回，本地规划器一般为空。</summary>
    public string Strategy = string.Empty;

    /// <summary>0~1 置信度；本地规划器默认 1。</summary>
    public float Confidence = 1f;

    /// <summary>对应的可执行命令；为 null 表示只做决策不执行。</summary>
    public IAICommand Command;

    public bool IsValid => ActionIndex >= 0;
}

/// <summary>
/// 命令模式接口（文档第 4 章 ICommand）：包含 Execute() 与 Undo()。
/// 命名加 AI 前缀是为了避免和项目里已有的类型重名。
/// </summary>
public interface IAICommand
{
    /// <summary>动作的可读描述，用于日志。</summary>
    string Description { get; }

    /// <summary>是否支持撤销。当前实现里只有“选目标阶段”可回滚，见 TargetManager。</summary>
    bool CanUndo { get; }

    /// <summary>执行该动作。</summary>
    bool Execute();

    /// <summary>撤销该动作。不支持撤销时返回 false。</summary>
    bool Undo();
}
