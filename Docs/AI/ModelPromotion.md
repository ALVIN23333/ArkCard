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

只有正式竞技场通过后，才执行正式替换。流水线会拒绝缺少 `arena-result.json` 或
`promotable != true` 的发布请求：

```powershell
arkcard-pipeline promote --config configs/round.toml
```

该命令会：

1. 检查正式竞技场的 `formal.passed`。
2. 把当前 `Assets/AI/Models/` 中已有的模型备份到本轮 `champion-backup/`。
3. 将同一次候选导出的三个文件复制到 `Assets/AI/Models/`。
4. 在 Unity 批处理模式执行 `ModelPromotionUtility.RefreshDefaultConfig`。
5. 生成 `promotion-report.json`，记录版本、源目录、目标目录、备份文件和正式竞技场结果。

发布后仍需回读 `DefaultAIModelConfig.asset` 的模型版本、schema、模型引用和运行时 checksum，
再运行完整 EditMode 测试和发布场景 smoke，最后才提交冠军模型与配置资产。需要回滚时，
将本轮 `champion-backup/` 中的三个文件恢复到 `Assets/AI/Models/`，再重新执行 Unity
配置刷新菜单或批处理方法。

严禁把失败候选复制到默认目录，或仅凭训练 loss、20 局 smoke、ONNX parity 判定晋级。
