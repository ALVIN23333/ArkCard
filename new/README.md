# new/ 改动清单（AI 代码对齐《AI集成方案设计文档》）

本目录是**只读副本 + 修改版**，原项目文件一律未改动。审阅通过后再把对应文件手工搬回原路径。

## 一、文件对照表

| new/ 下的文件 | 原文件路径 | 改动 | 类型 |
| --- | --- | --- | --- |
| `CardBattleAgent.cs` | `Assets/Scripts/AI/ML/Agents/CardBattleAgent.cs` | 改动 1：奖励函数 | 替换 |
| `AIController.cs` | `Assets/Scripts/Controller/AIController.cs` | 改动 2：调用时机 + 置信度阈值 | 替换 |
| `LLMStrategyClient.cs` | `Assets/Scripts/AI/LLM/LLMStrategyClient.cs` | 改动 2 配套：提示词 + confidence 缺省值 | 替换 |
| `IAIStrategy.cs` | — | 改动 4：`IAIStrategy` / `AIAction` / `IAICommand` | 新增 |
| `CommandInvoker.cs` | — | 改动 4：命令模式执行器 | 新增 |
| `AICommandMapper.cs` | — | 改动 4：动作 → 命令映射 | 新增 |
| `AdaptiveAIStrategy.cs` | — | 改动 4：策略门面（规划器 + LLM 降级） | 新增 |

建议落地位置：替换的三个文件回到原路径；新增的四个文件放到 `Assets/Scripts/AI/LLM/`（或新建 `Assets/Scripts/AI/Contracts/`），都会编译进 `Assembly-CSharp`，无需改 asmdef。

## 二、改动 1：奖励函数（文档 2.4 节逐条对应）

`CardBattleAgent.OnActionReceived` 在执行动作前后各取一次快照，用快照差还原整张奖励表，不再需要自己维护"上一回合状态"字段。

| 文档 2.4 奖励事件 | 文档值 | 实现位置 |
| --- | --- | --- |
| 对敌方英雄造成伤害 | +0.1 × 伤害值 | `enemyBefore.Health - enemyAfter.Health` |
| 己方英雄受到伤害 | -0.1 × 伤害值 | `myBefore.Health - myAfter.Health` |
| 消灭敌方随从 | +0.2 | `enemyBefore.Field.Count - enemyAfter.Field.Count` |
| 己方随从死亡 | -0.15 | `myBefore.Field.Count - myAfter.Field.Count` |
| 打出卡牌 | +0.05 | `action.Type == PlayHandCard` |
| 使用技能 | +0.05 | `action.Type == UseFieldCast` |
| 每步微小负奖励 | -0.001 | 每次调用固定叠加 |
| 回合结束且造成伤害 | +0.2 | `EndTurn && enemyHeroDamage > 0` |
| 获胜 / 失败 / 平局 | +1.0 / -1.0 / 0 | `after.IsGameOver` 分支 |
| （文档 2.3）非法动作 | 负奖励 | 保持原有 `-0.02` |

已删除原有的 `healthDelta` 单行奖励，避免与"英雄受伤/造成伤害"两项重复计分。

**训练侧要同步的一件事**：奖励尺度整体放大约一个量级（单次攻击最高可到 +0.6），
`Assets/AI/Configs/mlagents_card_battle.yaml` 的 `learning_rate: 0.0003` 建议先降到 `0.0001`，
或先只保留 ±0.05 档小奖励跑一轮观察曲线。

## 三、改动 2：LLM 调用时机与降级

| 文档要求 | 实现 |
| --- | --- |
| 3.1 开局时调用 | `snapshot.RootEndTurnCount <= 0` |
| 3.1 每 3 回合调用 | `RootEndTurnCount % llmStrategyIntervalTurns == 0`（默认 3） |
| 3.1 劣势时调用 | `me.Health < enemy.Health * llmDisadvantageHealthRatio`（默认 0.5） |
| 3.1 频率 3~5 次/局、超时 ≤3s | 上述三条共同限频；`LLMStrategyClient.timeoutSeconds` 默认 8，如需严格对齐可改 3 |
| 3.3 `confidence < 0.6` 回退 | `hint.confidence >= llmConfidenceThreshold`（默认 0.6）才采纳 |
| 3.4 网络失败 / JSON 错 / 下标越界 → 降级 | `LLMStrategyClient` 原本已返回 null；`AIController` 保留本地 MCTS 结果 |
| 3.5 System Prompt（角色 + 规则 + 格式） | 改写为常量 `SystemPrompt`，补上游戏规则段 |

`LLMStrategyHint.confidence` 缺省值从 `0f` 改为 `1f`：否则模型不返回该字段时
JsonUtility 会填 0，导致新加的阈值判断把所有建议都丢掉。

**安全性**：场景里 `player2/enableLLMStrategy` 目前是关闭的。即使打开，只要环境变量
`ARKCARD_LLM_API_KEY` 未设置，`RequestHint` 会立刻返回 null，AI 行为与现在完全一致。

## 四、改动 4：接口预留（文档第 4 章）

新增 4 个文件，**不替换**任何现有链路，纯新增：

```
IAIStrategy            文档第 4 章的 IAIStrategy：Task<AIAction> DecideAsync(GameState)
AIAction               决策结果：ActionIndex / Strategy / Confidence / Command
IAICommand             文档第 4 章的 ICommand：Execute() 与 Undo()（加 AI_ 前缀避免重名）
CommandInvoker         文档第 4 章的 CommandInvoker：命令历史 + UndoLast
AICommandMapper        文档第 4 章的 AICommandMapper：动作/下标 → 命令
AdaptiveAIStrategy     FallbackAI 的角色：规划器结果 + LLM 建议（带阈值）→ AIAction
```

两点设计说明（写进实验报告即可自圆其说）：

1. 文档写的是 `GameState`，实现里用项目已有的不可变快照 `BattleStateSnapshot`——它就是文档语义下的 GameState，不必再造一层。
2. 文档的 `FallbackAI.cs` 在项目里已有等价物：`ResilientAIPlanner`（神经 MCTS 失败自动回退旧 MCTS）。`AdaptiveAIStrategy` 把"规划器 + LLM + 降级"收拢到一个 `IAIStrategy` 门面下，等价于文档的 FallbackAI 职责。

## 五、没有做的两件事（建议在文档里改字，而不是改代码）

| 文档写法 | 项目实际 | 建议 |
| --- | --- | --- |
| 观察空间 97 维 | `AIEncodingSchema.StateFeatureCount` = 2180 维 | 文档加一句"实现阶段改用状态编码器，维度随效果枚举扩展" |
| 动作空间 Multi-Discrete `[10,4,12]` | 单离散分支 64，动作 = 合法动作列表下标 | 文档加一句"实现阶段改为合法动作索引单分支，避免 480 组合中大量非法组合" |

这两处要对上代码需要改编码器 + 黄金向量 + 全部编码测试 + Python 侧维度 + 重新训练，
工作量约 3 天且有搞坏现有可运行 AI 的风险；改文档两行字即可，且理由（变长合法动作、
多目标组合）本身站得住。

## 六、搬运后的验证步骤

1. 把 3 个替换文件 + 4 个新增文件放回 / 放入 `Assets/`。
2. 让 Unity 重新编译，检查 Console 无 `error CS`（当前基线：`Logs/BuildMLAgents.log` 是干净的）。
3. 跑 EditMode 测试（`Assets/Tests/EditMode`），确认 AI 相关测试未回归。
4. 进 Play Mode 打一局，确认 AI 仍能正常出牌（此时走的是模型缺失 → 旧 MCTS 回退链路）。
5. 若要用 ML-Agents 训练，先按第二节把 yaml 的 `learning_rate` 调整后再启动 PPO。
