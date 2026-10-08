# ML-Agents 场景配置与使用

## 运行时切换

`AIController` 的 Control Mode 有两档：

- `LocalPlanner`（默认）：神经 MCTS，失败回退旧 MCTS。`CardBattleAgent` 保持禁用。
- `MLAgents`：由 `CardBattleAgent` 按合法动作下标出牌。`AIController` 仍用 `actionInterval` 节拍调用 `RequestDecision()`。`DecisionRequester` 保持禁用，避免固定 5 步连打。

未连接 `mlagents-learn` 且 Behavior Parameters 没有 ONNX 时，会警告并回退 `LocalPlanner`，不会使用 Heuristic 的动作 0。

`enableLLMStrategy` 只在推理时覆盖两档；训练（Academy communicator 已连接）时不请求 LLM。

## 场景配置

1. 打开 `BattleScene`。
2. 执行菜单 `ArkCard > AI > Configure ML-Agents on AI Player`。
3. 在 `player2` 上确认 `CardBattleAgent`、`Behavior Parameters`。
4. 行为名 `CardBattleAgent`，向量观察大小 `2180`，离散动作分支大小 `64`。

## 训练

1. 将 `AIController` Control Mode 设为 `MLAgents`。
2. Behavior Type 用 `Default`，Model 可留空。
3. 进入 Play Mode。
4. 在项目根目录运行：

```text
mlagents-learn Assets/AI/Configs/mlagents_card_battle.yaml --run-id=card_battle_ppo
```

5. 现有 YAML 是 PPO smoke（`max_steps: 10000`），不是 AlphaZero / Barracuda 那条线。

## 推理

1. 把 `results/card_battle_ppo/CardBattleAgent.onnx` 赋给 Behavior Parameters 的 Model。
2. Behavior Type 设为 `Inference Only`。
3. Control Mode 设为 `MLAgents`。
4. 可选开启 `enableLLMStrategy` 做低频覆盖。
5. 合法动作仍由 `BattleStateSimulator` 校验后再执行。
