# 神经 MCTS 训练

## 第一次应该做什么

先在 Unity 中运行完整 EditMode 测试，确认规则模拟器、合法动作、隐藏信息和编码测试全部通过。规则测试没有通过时不要生成训练数据，因为模型会稳定学习模拟器错误。

随后生成至少 100,000 个教师决策样本。Unity 负责对局、旧 MCTS 根访问分布和终局 outcome；GPU 机器只负责读取分片、训练和导出 ONNX。

## 目录职责

- `Assets/Scripts/AI/ML/`：发布端编码、Barracuda 推理、PUCT 和旧 MCTS 回退。
- `Assets/Scripts/Editor/AITraining/`：Unity 教师对局、分片写入、竞技场和模型晋级工具。
- `Tools/AITraining/`：GPU 机器上的 PyTorch 训练、数据检查和 ONNX 导出。
- `Artifacts/AI/`：数据集、checkpoint 和报告。整个目录被 Git 忽略。
- `Assets/AI/Models/`：只放已通过晋级流程的 `policy.onnx`、`value.onnx`、`manifest.json`。
- `Assets/AI/Configs/DefaultAIModelConfig.asset`：Unity 发布配置。未训练阶段模型引用为空，因此自动回退旧 MCTS。

## 生成教师数据

关闭正在占用工程的 Unity 编辑器后，可使用命令行批处理。`Unity.exe` 路径按本机 Unity Hub 安装位置调整。

```powershell
Unity.exe -batchmode -quit `
  -projectPath D:\unityXiangmu\ArkCard `
  -executeMethod TrainingMatchRunner.RunFromCommandLine `
  -aiSamples 100000 `
  -aiShardSamples 2048 `
  -aiSeed 20260806 `
  -aiOutput Artifacts/AI/Datasets/teacher-v1
```

编辑器菜单 `Tools > AI Training > Generate 2K Teacher Smoke Dataset` 只用于快速冒烟验证，不满足正式训练数量。

分片扩展名为 `.arkds.gz`。每条记录包含格式/schema 版本、随机种子、game/ply、观察方、两副牌、状态特征、全部合法动作特征、根访问次数和终局 outcome。每个分片旁边有样本数与 SHA-256 manifest。

## GPU 机器训练

```bash
cd Tools/AITraining
python -m venv .venv
source .venv/bin/activate
pip install -e '.[export,dev]'

arkcard-inspect-dataset /data/arkcard/teacher-v1
arkcard-train /data/arkcard/teacher-v1 \
  --output /checkpoints/arkcard/v1 \
  --epochs 20 \
  --batch-size 64 \
  --device cuda

arkcard-export /checkpoints/arkcard/v1/latest.pt \
  --output /exports/arkcard/v1 \
  --model-version v1-teacher-001
```

训练损失为根访问分布交叉熵、终局值 Huber loss 和 AdamW 权重衰减。策略头按每条记录的可变候选动作集合归一化，价值目标固定为当前观察方胜 `+1`、负 `-1`、平 `0`。

导出固定使用 opset 9。策略输入/输出名为 `policy_input` / `policy_logit`，价值输入/输出名为 `state_input` / `value`。导出脚本默认用 ONNX Runtime 与 PyTorch 对比，最大绝对误差超过 `1e-4` 时直接失败。

## 自博弈阶段

教师模仿模型通过初始竞技场后，再把教师规划器替换为带根 Dirichlet 噪声的神经 MCTS 生成新数据。正式发布搜索必须关闭根噪声，并选择最高根访问次数动作。每轮训练数据、checkpoint、模型和竞技场报告使用不可变版本号，不能覆盖当前冠军。

先将当前冠军模型导入并刷新 `DefaultAIModelConfig.asset`，再运行：

```powershell
Unity.exe -batchmode -quit `
  -projectPath D:\unityXiangmu\ArkCard `
  -executeMethod TrainingMatchRunner.RunSelfPlayFromCommandLine `
  -aiSamples 100000 `
  -aiSeed 20260806 `
  -aiModelConfig Assets/AI/Configs/DefaultAIModelConfig.asset `
  -aiOutput Artifacts/AI/Datasets/self-play-v1 `
  -aiPrefix neural-self-play-v1
```

自博弈入口直接创建神经规划器，不允许旧 MCTS 回退；模型或推理失败会终止该轮数据生成。根先验加入 `alpha=0.3`、占比 `0.25` 的 Dirichlet 噪声，写出的监督标签仍是根访问分布。
