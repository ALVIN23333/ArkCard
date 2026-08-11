# ArkCard AI 训练流程与原理

## 1. 系统定位

当前 AI 不是 Unity ML-Agents 的在线强化学习。项目虽然安装了
`com.unity.ml-agents`，但 `Assets/` 中没有 `Agent`、`Academy` 或
`BehaviorParameters` 的使用代码。

实际实现是一套自研的 AlphaZero 风格离线流水线：

```text
旧启发式 MCTS 教师
  -> Unity 生成 (状态, 合法动作, 根访问次数, 终局结果)
  -> PyTorch 同时训练策略头和价值头
  -> 导出 policy.onnx / value.onnx
  -> Unity Barracuda 推理
  -> 神经网络先验 + PUCT 搜索
  -> 配对竞技场晋级
  -> 带根噪声的神经 MCTS 自博弈
  -> 合并新旧数据继续训练
```

它不是“网络直接出牌”。网络只负责给合法动作先验概率和局面价值；
最终动作仍由 MCTS 在规则模拟器中搜索后决定。

## 2. 目录职责

- `Assets/Scripts/AI/`：快照、规则模拟、旧 MCTS 和神经 MCTS。
- `Assets/Scripts/AI/ML/Encoding/`：状态和动作特征编码。
- `Assets/Scripts/AI/ML/Inference/`：Barracuda 模型加载、契约检查和推理。
- `Assets/Scripts/Editor/AITraining/`：教师/自博弈数据、分片、竞技场和模型配置刷新。
- `Tools/AITraining/`：PyTorch 训练、数据检查、ONNX 导出和流水线编排。
- `Artifacts/AI/`：本机数据集、checkpoint、日志和报告，不提交 Git。
- `Assets/AI/Models/Candidate/`：尚未晋级的候选模型。
- `Assets/AI/Models/`：通过晋级后用于发布的冠军模型。

## 3. 教师数据如何产生

`TrainingMatchRunner.RunFromCommandLine` 在 Unity 编辑器批处理模式中运行纯快照对局。
每个决策点执行以下过程：

1. 从权威状态生成当前行动方的观察，清除对手手牌和牌库明牌，保留隐藏牌池。
2. 旧 MCTS 对隐藏信息做 determinization，并用规则模拟器展开合法动作。
3. 保存所有合法动作及根节点访问次数，而不只保存最终选中的动作。
4. 执行选中动作，直到胜负或 `maxPlies` 截断。
5. 对整局样本回填观察方 outcome：胜 `+1`、负 `-1`、平/截断 `0`。

默认教师搜索基线为 `300 iterations / 35 ms / rollout 4`。多牌组模式通过
`deckMatrix = "all"` 遍历牌组组合，并交替交换双方牌组，降低固定牌组和先后手偏差。

分片格式为 `.arkds.gz`。每条记录包括 schema、seed、game/ply、观察方、双方牌组、
状态特征、可变长度合法动作矩阵、各动作访问次数和 outcome。写入器会检查维度、有限值、
访问质量，并为每个分片生成样本数和 SHA-256 manifest。

## 4. 网络学到什么

schema v2（效果枚举扩展到 34 种）的固定维度为：

| 输入 | 维度 | 含义 |
| --- | ---: | --- |
| 状态 `s` | 2180 | 回合/双方标量、己方手牌与场面、对方场面、墓地/牌库/隐藏牌池摘要 |
| 动作 `a` | 739 | 动作类型、来源卡、目标数量和最多 6 个目标的语义特征 |
| 策略输入 `(s,a)` | 2919 | 同一状态分别与每个合法动作拼接 |

策略网络输出每个 `(s,a)` 的一个 logit，并只在该状态的合法动作集合内做 softmax。
监督目标是旧/神经 MCTS 根访问分布：

```text
pi(a|s) = visit_count(a) / sum_a visit_count(a)
```

价值网络只读状态，输出 `tanh` 限制的 `[-1, 1]` 标量。总损失为：

```text
policy cross entropy + value_weight * Huber(value, outcome)
```

优化器为 AdamW，另有梯度裁剪；CUDA 可启用 AMP。数据按完整 `game_id` 做确定性
训练/验证划分，防止同一对局相邻局面跨集合泄漏。每轮保存 `latest.pt`，验证损失改善时
保存 `best.pt`；history 每个 epoch 原子落盘，续训时检查 epoch 连续性。

## 5. ONNX 与 Unity 推理

导出固定使用 Barracuda 兼容的 opset 9：

- 策略：`policy_input -> policy_logit`，动态维是合法动作数。
- 价值：`state_input -> value`。

导出器检查 checkpoint schema，并比较 PyTorch 与 ONNX Runtime；可通过
`parityRecords` 使用真实数据样本，最大绝对误差不得超过 `1e-4`。manifest 记录模型版本、
schema、维度、张量名和两个 ONNX 文件的合并 SHA-256。

Unity 中 `AIModelConfig.Validate` 检查 schema、模型引用、运行时 checksum 和搜索参数；
`BarracudaPolicyValueProvider` 再检查输入/输出名称及维度。任一步失败时，正式对局的
`ResilientAIPlanner` 会整次决策回退旧 MCTS，并只记录一次明确警告。训练自博弈不允许回退，
避免把“神经模型失败后的旧策略”混入神经自博弈数据。

## 6. 神经 MCTS 原理

每次搜索先对隐藏信息采样多个可能世界，再在每个世界运行 PUCT：

```text
score = Q(s,a) + C_puct * P(s,a) * sqrt(N(s)) / (1 + N(s,a))
```

- `P` 来自策略网络，是动作先验。
- `Q` 是搜索过程中回传的平均价值。
- `N` 是访问次数，控制探索与利用的平衡。
- 叶节点价值来自价值网络，终局节点直接使用真实胜负。
- 多个 determinization 的根统计聚合后，正式运行选择访问次数最高的动作。

自博弈阶段在根先验加入 `Dirichlet(alpha=0.3)`、占比 `0.25` 的噪声以制造探索；
发布和竞技场关闭根噪声。

## 7. 推荐执行入口

每轮复制一份 `Tools/AITraining/configs/round.toml`（如 `configs/round2.toml`），
使用不可变的模型版本和 run 目录。效果扩展后的 schema v2 轮次使用
`configs/round2.toml`：

```powershell
cd Tools/AITraining
python -m pip install -e ".[export,dev]"

# 可单独生成旧 MCTS 教师数据
arkcard-pipeline generate-teacher --config configs/round2.toml

# 按 data.mode 选择 teacher/selfplay/skip，然后训练
arkcard-pipeline train --config configs/round2.toml

# 训练、导出并隔离暂存候选；默认从 best.pt 导出
arkcard-pipeline candidate --config configs/round2.toml

# 先跑 smoke，再跑正式候选 vs 当前冠军竞技场
arkcard-pipeline arena --config configs/round2.toml
```

正确晋级顺序是：候选模型写入 `Assets/AI/Models/Candidate/`，刷新
`CandidateAIModelConfig.asset`，对 `DefaultAIModelConfig.asset` 当前冠军运行配对竞技场；
只有正式报告通过后，才允许替换默认冠军模型。不要在竞技场之前覆盖冠军。

正式门槛为至少 1000 局、平局计半分后的候选得分率不低于 55%、候选决策 P95 不超过
50 ms，并且规则与编码回归全部通过。双方使用配对随机种子，并交换先后手和牌组。

## 8. 当前工程状态（2026-08-11）

- 效果枚举新增统一可配置效果 `Damage=110` 至 `Silence=121`（共 12 个），
  `EffectType` 从 22 种扩展到 34 种；`AIEncodingSchema` 已提升到 schema v2，
  状态/动作/策略输入维度变为 `2180 / 739 / 2919`。
- 旧 schema 1 的 `teacher-v1`、`self-play-r001` 数据集和 `teacher-v1-001`
  checkpoint 与 schema v2 不兼容；`AIModelConfig.Validate` 会拒绝旧模型，
  正式对局回退旧 MCTS，直到新模型按候选/竞技场流程晋级。
- 新一轮使用 `Tools/AITraining/configs/round2.toml`：先以 `data.mode = "teacher"`
  重新生成 `teacher-v2` 数据，从零训练（无 `resume`），再导出并进入候选/竞技场流程。
- 活动场景为 `Assets/Scenes/BattleScene.unity`，`player2` 挂载 `AIController`；
  场景仍引用旧冠军配置 `192 iterations / 45 ms / 4 determinizations`，等待新模型替换。

schema 1 首轮状态（2026-08-08）：模型版本 `teacher-v1-001`，ONNX 随机输入 parity
策略最大误差约 `3.81e-6`；`arena-smoke.json` 20 局 5 胜 15 负（得分率 25%），
延迟通过但胜率和正式局数未达标，因此该模型不能视为已晋级。

## 9. 仍需补强的部分

1. 先查明当前候选 25% 得分率的原因，重点比较策略交叉熵/价值验证损失、教师数据牌组分布、
   截断平局比例，以及固定局面的策略排序；在 smoke 达标前不要生成神经自博弈数据。
2. 竞技场当前只支持一对配置牌组并交换双方；训练已经支持牌组矩阵，正式晋级也应扩展为
   多牌组分层报告，避免总分掩盖某个牌组的系统性退化。
3. Python 数据检查器会完整解码并校验记录，但尚未自动核对每个 Unity sidecar manifest 的
   SHA-256；正式训练仍需保留并额外校验数据集校验和清单。
4. 导出已支持真实样本的 PyTorch/ONNX Runtime parity，但尚无固定黄金样本的
   ONNX Runtime/Barracuda 数值逐项对比；当前竞技场只能提供端到端间接覆盖。
5. 自博弈只有根 Dirichlet 噪声，最终动作始终取最高访问次数；可在前若干 ply 按访问分布
   加温度采样，后期再退火到 argmax，以增加状态覆盖。
6. checkpoint 尚未保存 AMP scaler 和 Python/NumPy/PyTorch RNG 状态；续训语义正确，
   但中断后不能做到逐 bit 复现。
