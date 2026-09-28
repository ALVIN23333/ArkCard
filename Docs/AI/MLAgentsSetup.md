# ML-Agents 场景配置

1. 打开 `BattleScene`。
2. 执行菜单 `ArkCard > AI > Configure ML-Agents on AI Player`。
3. 在 `player2` 上确认 `CardBattleAgent`、`Behavior Parameters` 和 `Decision Requester`。
4. 使用行为名 `CardBattleAgent`，向量观察大小为 `2180`，离散动作分支大小为 `64`。
5. 先用 Heuristic 或 Default 模式确认无 Console 错误，再使用
   `Assets/AI/Configs/mlagents_card_battle.yaml` 启动 PPO 训练。

训练模型导入后，在 Behavior Parameters 的 Model 字段中指定 ONNX。正式卡牌 AI
仍通过 `AIController` 的神经 MCTS 执行动作，ML-Agents Agent 是可独立训练和
验证的环境桥接，不会绕开 `BattleStateSimulator` 的合法动作校验。