# 模型晋级

## 隔离候选

候选模型不能在竞技场前覆盖当前冠军。推荐使用：

```powershell
arkcard-pipeline candidate --config Tools/AITraining/configs/round.toml
```

该命令训练或读取 checkpoint，导出并校验 `policy.onnx`、`value.onnx`、`manifest.json`，
然后把它们暂存到 `Assets/AI/Models/Candidate/`，并通过 Unity 创建或刷新
`Assets/AI/Configs/CandidateAIModelConfig.asset`。当前冠军继续保留在
`Assets/AI/Models/` 和 `DefaultAIModelConfig.asset`。

## 竞技场门槛

```powershell
arkcard-pipeline arena --config Tools/AITraining/configs/round.toml
```

流程先运行小规模 smoke，smoke 只要求完成配置的 smoke 局数，并检查得分率和 P95；
通过后才运行正式竞技场。正式竞技场默认要求：

- 至少 1000 局；
- 平局计半分后候选得分率不低于 55%；
- 发布 Windows CPU 上候选决策 P95 不超过 50 ms；
- 全部规则、隐藏信息、编码和模型回退测试通过。

候选与当前冠军复用配对随机种子，并交换先后手和双方牌组。模型缺失、schema/维度/checksum
错误、非有限输出或推理异常均应使检查失败；正式对局运行时则会回退旧 MCTS。

## 正式替换冠军

只有 `arena-result.json` 的 `promotable` 为 `true` 后，才执行正式替换：

1. 归档本轮配置、Git commit、数据校验和、checkpoint、训练 history、导出 manifest 和竞技场报告。
2. 将同一次候选导出的三个文件复制到 `Assets/AI/Models/`。
3. 在 Unity 执行 `Tools > AI Training > Refresh Default Config From Promoted Models`。
4. 回读 `DefaultAIModelConfig.asset` 的模型版本、schema、模型引用和运行时 checksum。
5. 再运行完整 EditMode 测试和发布场景 smoke，最后才提交冠军模型与配置资产。

严禁把失败候选复制到默认目录，或仅凭训练 loss、20 局 smoke、ONNX parity 判定晋级。
