# ArkCard 混合 AI 集成

## 运行时结构

`AIController` 负责节拍和最终动作入口，并用 Control Mode 选择策略：

- `LocalPlanner`（默认）：`SnapshotFactory` 生成不可变局面，`BattleStateSimulator` 列出合法动作，再由 Barracuda 神经 MCTS 搜索；模型不存在或推理失败时回退旧 MCTS。
- `MLAgents`：同样的节拍下调用 `CardBattleAgent.RequestDecision()`。离散动作是当前合法动作列表的下标，执行前仍做规则校验。没有 trainer、也没有 ONNX 时回退本地规划器。

启用 `enableLLMStrategy` 后，推理路径会低频调用智谱兼容 Chat Completions。请求包含双方生命、费用、场面数量和带卡种/目标描述的合法动作；响应只允许 `action_index`、策略说明和置信度。索引越界、JSON 错误、超时、网络失败或置信度不足都会丢弃提示。训练时（`mlagents-learn` 已连接）不请求 LLM。API Key 只从环境变量 `ARKCARD_LLM_API_KEY` 读取。

## ML-Agents 配置

将 `CardBattleAgent` 挂到 AI 玩家，Behavior Parameters 行为名 `CardBattleAgent`，向量观察 `2180`，一条离散分支大小 `64`。用 `Assets/AI/Configs/mlagents_card_battle.yaml` 做 PPO。导出的 ONNX 只赋给 Behavior Parameters；Barracuda 的 policy/value 模型仍走原来的 schema 和校验。

详细步骤见 `Docs/AI/MLAgentsSetup.md`。

## 验证清单

1. Control Mode 为 `LocalPlanner` 时，未设置 `ARKCARD_LLM_API_KEY` 应无网络请求，并正常使用本地规划器。
2. 设置 Key 后观察请求 JSON 和返回 JSON，确认 `action_index` 在合法动作范围内。
3. Control Mode 为 `MLAgents` 且未挂模型、未连 trainer 时，应警告并回退规划器。
4. 观察写入长度必须始终为 2180。挂上 ONNX 并选 Inference Only 后，Agent 应能出牌且不与 MCTS 抢步。
5. Barracuda policy/value 模型仍遵循原有 schema 和校验流程。
