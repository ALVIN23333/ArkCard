# 补全 AI 集成源码（ML-Agents + LLM）

## 背景

原 `main` 分支缺少 AI 集成相关代码：LLM 战术建议通道、ML-Agents Agent、
自研训练链路的配置与文档均未提交。本 PR 从工作区整理并补全这部分源码，
同时修正了若干依赖引用与调用逻辑问题。

## 新增文件（37 个）

### 1. LLM 战术建议通道

| 文件 | 说明 |
| --- | --- |
| `Assets/Scripts/AI/LLM/LLMStrategyClient.cs` | 大语言模型 API 调用与解析（466 行） |
| `Assets/Scripts/AI/LLM/IAIStrategy.cs` | `IAIStrategy` / `AIAction` / `IAICommand` 接口预留 |
| `Assets/Scripts/AI/LLM/CommandInvoker.cs` | 命令模式执行器（支持撤销） |
| `Assets/Scripts/AI/LLM/AICommandMapper.cs` | 动作 → 命令映射 |
| `Assets/Scripts/AI/LLM/AdaptiveAIStrategy.cs` | 策略门面（规划器 + LLM + 降级） |

设计要点：

- API Key 只从环境变量 `ARKCARD_LLM_API_KEY` 读取，**代码与场景中均不硬编码**
- 请求体**手工拼装**，规避 `JsonUtility` 的两处硬限制：
  - `temperature` 会被打印成 17 位小数（`0.10000000149011612`），服务端返回 `code 1210`
    「限制小数点 [2] 位」→ 改用 `ToString("F2")` 固定 2 位
  - `JsonUtility.ToJson` 对**顶层数组**会序列化成 `{}`，导致 `messages` 字段非法
    → 手工拼装为真数组
- `ParseHint` 内置 `StripCodeFence()`，剥离部分模型返回的 Markdown 代码块围栏
- 失败日志打印**请求体与响应体**，便于定位服务端 4xx 原因

### 2. ML-Agents Agent

| 文件 | 说明 |
| --- | --- |
| `Assets/Scripts/AI/ML/Agents/CardBattleAgent.cs` | Agent 实现（观察/动作/奖励/回合） |
| `Assets/Scripts/Editor/AITraining/MLAgentsSceneSetup.cs` | 一键挂载 Agent 组件的编辑器菜单 |
| `Assets/AI/Configs/mlagents_card_battle.yaml` | PPO 训练超参配置 |

- 观察空间：`AIEncodingSchema.StateFeatureCount` = **2180 维**语义编码
- 动作空间：**单离散分支 64**，语义为"合法动作列表下标"
- 训练环境：独立构建 `Builds/MLAgentsTraining/`

### 3. 文档与配置

| 文件 | 说明 |
| --- | --- |
| `README.md` | 项目概览 + 三档配置指引 + 目录结构 + 文档索引 |
| `Docs/项目运行配置清单.md` | 克隆后必读：分档配置、常见问题、完整命令序列 |
| `Docs/环境变量与密钥管理.md` | 密钥设置方式、防止误提交、泄露后处置 |
| `Docs/AI/HybridIntegration.md` | 运行时 AI 结构（规划器 + LLM + 降级） |
| `Docs/AI/MLAgentsSetup.md` | ML-Agents 场景配置步骤 |
| `.env.example` | 环境变量模板（**仅含变量名，无真实密钥**） |
| `new/` | AI 改动的独立留档副本（8 个文件） |

## 修改文件（14 个）

| 文件 | 改动 |
| --- | --- |
| `Assets/Scripts/ArkCard.Runtime.asmdef` | 增加 `Unity.ML-Agents` 引用 |
| `Assets/Scripts/Editor/ArkCard.Editor.asmdef` | 增加 `Unity.ML-Agents` 引用 |
| `Assets/Scripts/Controller/AIController.cs` | LLM 调用限频与置信度阈值 |
| `Assets/Scenes/BattleScene.unity` | 挂载 AI 相关组件、保存开关状态 |
| `.gitignore` | 增加密钥与生成物排除规则 |
| 训练链路若干 | `ModelPromotionUtility`、`pipeline.py`、`train.py`、`round2.toml` 等 |

### `AIController` 的两处逻辑修正

**① LLM 调用限频。** 原实现依赖 `snapshot.RootEndTurnCount` 判断调用时机，
但该字段只在 `BattleStateSimulator` 的内部副本上递增，`SnapshotFactory` 构造正式对局
快照时从不设置它（恒为 0），导致「每 N 回合」永不生效、每次决策都发起调用。
现改为在 `EndTurn()` 中累加本地计数器 `completedAiTurns`：

```csharp
private int completedAiTurns;                       // 已完成 AI 回合数

public override void EndTurn()
{
    base.EndTurn();
    nextActionTime = 0f;
    completedAiTurns++;
}

// ShouldRequestLLMHint 内
if (completedAiTurns <= 0) return true;                              // 开局调用一次
if (completedAiTurns % llmStrategyIntervalTurns == 0) return true;   // 每 N 回合
return me.Health < enemy.Health * llmDisadvantageHealthRatio;        // 劣势
```

**② 置信度阈值。** 采纳 LLM 建议前增加判断，低于阈值则保留本地规划器结果：

```csharp
if (hint != null
    && hint.confidence >= llmConfidenceThreshold    // 默认 0.6
    && hint.action_index >= 0
    && hint.action_index < legalActions.Count)
{
    selectedAction = legalActions[hint.action_index];
}
```

## 验证

| 项目 | 结果 |
| --- | --- |
| Unity 编译 | 无 `error CS`（日志 `error CS` 计数为 0） |
| EditMode 测试 | 约 120 项通过 |
| PlayMode 测试 | 7 项通过 |
| Python 测试 | 19 项通过 |
| 训练数据集校验 | `inspect_dataset` 读取 49 分片 / 100002 样本，无头部不兼容 |
| LLM 通道实测 | 返回 HTTP 200，解析出 `action_index` / `strategy` / `confidence`，游戏内 `[AI Hybrid]` 日志确认建议被采纳 |
| 降级链路实测 | 请求失败时打印原因并自动回退规则 MCTS，对局不中断 |

## 编码维度一致性说明

C# 与 Python 两侧维度**已对齐**，可以直接重训，无需先改维度：

```
VisibleCardSlotCount    = 10 + 5 + 5 = 20        # 手牌 + 己方场面 + 敌方场面
CardSummarySlotCount    = 4
CardFeatureCount        = 90
StateFeatureCount       = 20 + (20 + 4) * 90 = 2180
ActionFeatureCount      = 5 + 6 + 1 + 90 + 1 + 6 * 106 = 739
PolicyInputFeatureCount = 2180 + 739 = 2919
```

对应关系：`AIEncodingSchema.cs` ↔ `arkcard_ai/dataset.py` ↔ `pipeline.py` 导出契约
↔ 磁盘数据集清单，四处均为 `2180 / 739 / 2919`。

## 已知状态与缺口（如实记录）

| 项 | 状态 |
| --- | --- |
| 基础玩法 | 已实现，可完整打完一局 |
| 右键撤销 | 部分实现：覆盖"选目标阶段 + 待打出卡牌" |
| 随从技能 | 机制齐全，但仅 1 张卡配置了启动技能；`castUsed` 目前只在召唤时重置 |
| 神经推理（Barracuda + 神经 MCTS） | **未启用**：仓库内 ONNX 为 schema v1，运行时为 v2；`Assets/AI/Models/` 为空。运行时自动回退规则 MCTS |
| LLM 战术建议 | 已打通：调用时机、置信度阈值、六类降级路径均可用 |
| 音效 | 音频资源已就位，尚未接线到游戏事件 |
| 粒子特效 | 未实现，战斗反馈仅使用 PrimeTween 补间 |

## 不包含的内容

`Library/`、`Builds/`、`Artifacts/`、`.venv/`、`*.csproj` 及任何密钥。
`.gitignore` 已排除这些路径，并已逐项验证本次提交无敏感文件。
