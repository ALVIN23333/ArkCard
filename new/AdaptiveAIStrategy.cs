// ============================================================================
// 【改动 4-4 / AdaptiveAIStrategy，对齐《AI集成方案设计文档》第 4 章】
//
// 文档第 4 章建议预留：
//   · FallbackAI.cs        # 降级规则 AI
//   · AICommandMapper.cs   # 将 LLM/ML-Agents 输出映射为 Command
//   · IAIStrategy：便于替换 ML-Agents 或 LLM
//
// 本类把项目里已有的三层实现收拢到一个 IAIStrategy 门面后面：
//   · 本地规划器 IAIPlanner（神经 MCTS，失败自动回退旧 MCTS）
//   · LLM 顾问 LLMStrategyHint（带 confidence 阈值）
//   · 降级规则：任何一层不可用时都退回本地规划器结果（文档 3.4 节第 5 步）
//
// 它不替换 AIController，只是给“AI 策略”提供一个与文档一致的入口。
// 依赖的 IAIPlanner / BattleStateSimulator / MCTSResult 都是项目现有类型。
// ============================================================================

using System.Collections.Generic;
using System.Threading.Tasks;

public sealed class AdaptiveAIStrategy : IAIStrategy
{
    private readonly IAIPlanner planner;
    private readonly BattleStateSimulator simulator = new();
    private readonly AICommandMapper mapper;

    public AdaptiveAIStrategy(IAIPlanner planner, AIActionExecutor executor, AIActionRollback rollback = null)
    {
        this.planner = planner;
        mapper = new AICommandMapper(executor, rollback);
    }

    /// <summary>策略名称（日志用）。</summary>
    public string Name => "AdaptiveAI(planner + optional LLM hint)";

    /// <summary>
    /// 决策入口：先让本地规划器搜索，再用 LLM 建议覆盖（仅在置信度达标时）。
    /// </summary>
    /// <param name="state">当前局面快照（文档语义里的 GameState）。</param>
    /// <param name="llmHint">LLM 顾问建议，可为 null。</param>
    /// <param name="confidenceThreshold">采纳 LLM 建议所需的最低置信度（文档默认 0.6）。</param>
    public Task<AIAction> DecideAsync(
        BattleStateSnapshot state,
        LLMStrategyHint llmHint = null,
        float confidenceThreshold = 0.6f)
    {
        if (state == null || planner == null)
        {
            return Task.FromResult<AIAction>(null);
        }

        List<SimulatedAction> legalActions = simulator.GenerateLegalActions(state);
        if (legalActions.Count == 0)
        {
            return Task.FromResult<AIAction>(null);
        }

        MCTSResult result = planner.Search(state);
        SimulatedAction selected = result != null ? result.SelectedAction : null;
        int selectedIndex = IndexOf(legalActions, selected);
        string strategy = string.Empty;
        float confidence = 1f;

        // 文档 3.3 节：confidence < 阈值时忽略 LLM 建议，使用本地模型/规划器决策。
        if (llmHint != null
            && llmHint.confidence >= confidenceThreshold
            && llmHint.action_index >= 0
            && llmHint.action_index < legalActions.Count)
        {
            selected = legalActions[llmHint.action_index];
            selectedIndex = llmHint.action_index;
            strategy = llmHint.strategy ?? string.Empty;
            confidence = llmHint.confidence;
        }

        if (selected == null)
        {
            return Task.FromResult<AIAction>(null);
        }

        AIAction action = new()
        {
            ActionIndex = selectedIndex,
            Strategy = strategy,
            Confidence = confidence,
            Command = mapper.Map(selected),
        };

        return Task.FromResult(action);
    }

    /// <summary>IAIStrategy 约定的无 LLM 版本：等价于纯本地规划器决策。</summary>
    public Task<AIAction> DecideAsync(BattleStateSnapshot state)
    {
        return DecideAsync(state, null, 0f);
    }

    private static int IndexOf(List<SimulatedAction> actions, SimulatedAction target)
    {
        if (target == null)
        {
            return -1;
        }

        for (int index = 0; index < actions.Count; index++)
        {
            if (target.Equals(actions[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
