# ArkCard 混合 AI 集成

## 运行时结构

`AIController` 仍然负责正式对局动作执行。每次决策先从 `SnapshotFactory`
生成不可变局面，再由 `BattleStateSimulator` 生成合法动作。默认使用
Barracuda 策略价值模型和神经 MCTS；模型不存在或推理失败时继续使用旧 MCTS。

启用 `AIController.enableLLMStrategy` 后，`LLMStrategyClient` 在一次 AI 决策
中低频调用智谱兼容 Chat Completions 接口。请求包含双方生命、费用、场面数量
和合法动作列表，响应只允许返回 `action_index`、策略说明和置信度。索引越界、
JSON 错误、超时或网络失败都会丢弃提示并保留本地 MCTS 结果。API Key 只从
环境变量 `ARKCARD_LLM_API_KEY` 读取。

## ML-Agents 配置

`CardBattleAgent` 使用状态编码器作为观察空间，离散动作是当前局面合法动作
列表的索引，执行前仍由本地规则模拟器校验。将它挂到 AI 玩家对象，添加
Behavior Parameters（行为名 `CardBattleAgent`，一条离散分支，分支大小至少
64）和 Decision Requester，即可使用 `Assets/AI/Configs/mlagents_card_battle.yaml`
进行 PPO 训练。训练完成后把 ONNX 模型配置到 Behavior Parameters；正式对局
仍可由 `AIController` 的神经 MCTS 负责最终动作选择。

## 验证清单

1. 未设置 `ARKCARD_LLM_API_KEY` 时，AI 应无网络请求并正常使用本地规划器。
2. 设置 Key 后观察请求 JSON 和返回 JSON，确认返回的 `action_index` 在合法动作范围内。
3. 用 Heuristic Only 运行 Agent，检查观察空间无异常，并用 YAML 开始 PPO 训练。
4. 训练导出的 ONNX 只作为 ML-Agents 行为模型导入；Barracuda 的 policy/value 模型仍遵循原有 schema 和校验流程。