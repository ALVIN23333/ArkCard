# 模型晋级

1. 从 GPU 机器取得同一次导出的 `policy.onnx`、`value.onnx` 和 `manifest.json`。
2. 将三者放入 `Assets/AI/Models/`，不要在该目录保留 checkpoint 或训练数据。
3. 在 Unity 执行 `Tools > AI Training > Refresh Default Config From Promoted Models`。
4. 工具先校验原始 ONNX 合并 SHA-256 和 schema，再同步导入 Barracuda，并更新 `Assets/AI/Configs/DefaultAIModelConfig.asset` 的模型引用、版本和运行时校验和。
5. 运行全部 EditMode 测试，并对 PyTorch、ONNX Runtime、Barracuda 的固定黄金输入比较输出，误差必须不超过 `1e-4`。
6. 运行配对竞技场。候选与旧 MCTS 交换先后手和牌组，并复用配对随机种子。

正式竞技场命令：

```powershell
Unity.exe -batchmode -quit `
  -projectPath D:\unityXiangmu\ArkCard `
  -executeMethod ArenaRunner.RunFromCommandLine `
  -aiArenaGames 1000 `
  -aiSeed 20260806 `
  -aiModelConfig Assets/AI/Configs/DefaultAIModelConfig.asset `
  -aiArenaReport Artifacts/AI/Reports/v1-arena.json
```

GPU 或 CI 机器可再次强制检查报告：

```bash
arkcard-check-arena Artifacts/AI/Reports/v1-arena.json
```

晋级门槛全部满足才允许提交 `Assets/AI/Models/` 和配置资产：至少 1,000 局；计平局半分后的候选得分率不低于 55%；发布 Windows CPU 上候选决策 P95 不超过 50 ms；全部规则回归测试通过。模型缺失、schema/维度/校验和错误、非有限输出或推理异常均会使整次决策回退旧 MCTS，并且只记录一次明确原因。
