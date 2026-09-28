# ArkCard —— 类炉石传说卡牌对战系统

> 一款 PC 端 1v1 回合制卡牌对战游戏（明日方舟卡牌）。玩家执先手（P1）与 AI（P2）对战，
> 支持费用增长、每回合抽卡、随从攻击与技能、法术目标选择、墓地复活与操作撤销。
> 内置 ML-Agents 环境桥接、自研 AlphaZero 风格训练链路，以及大语言模型（LLM）战术建议通道。

| 项目 | 值 |
| --- | --- |
| Unity 版本 | **2022.3.62f3c1** |
| 渲染管线 | Built-in |
| 主要依赖 | ML-Agents 2.0.2、Barracuda 3.0.0、Mirror、PrimeTween、Newtonsoft.Json |
| 卡牌规模 | 23 张（14 随从 + 9 法术），34 种效果类型 |
| AI 观察/动作空间 | 状态 2180 维 / 动作 739 维（策略输入 2919 维）；单离散动作分支 64 |
| 目标平台 | PC（鼠标键盘） |

---

## 一、克隆后如何跑起来

按需求分三档，**第 1 档是必需的，且不需要任何密钥**。

### 第 1 档：跑起游戏（必需）

1. **安装 Unity 2022.3.62f3c1**（必须该版本，见 `ProjectSettings/ProjectVersion.txt`）
2. **用 Unity Hub 打开本项目根目录**，等待首次导入完成（约 5~15 分钟）
3. **打开 `Assets/Scenes/BattleScene.unity`**
4. **点 Play**，即可与 AI 对战

首次打开时 Unity 会重新生成 `Library/`（未随仓库提交）并从网络拉取依赖。
`Packages/manifest.json` 中有 **4 个来自 Git 仓库的包，需要能访问外网**：

| 包 | 来源 |
| --- | --- |
| `cn.unity.uos.launcher` | `https://cnb.cool/unity/uos/UOSLauncher.git` |
| `com.gamebooom.unity.mcp` | `https://github.com/FunplayAI/funplay-unity-mcp.git#v0.6.2` |
| `com.mirror.steamworks.net` | `https://github.com/Chykary/FizzySteamworks.git?path=/com.mirror.steamworks.net` |
| `com.rlabrecque.steamworks.net` | `https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net` |

**Console 出现下面这行是正常的，不是故障：**

```
[AI ML] Falling back to legacy MCTS: AI model config is not assigned.
```

原因：模型权重位于 `Assets/AI/Models/`，该目录受 `.gitignore` 排除，未随仓库分发。
`AIPlannerFactory` 会捕获此情况并自动回退到规则 MCTS，游戏功能完整、不会崩溃。
神经推理需要按第 3 档重新训练并晋级模型后才可用。

### 第 2 档：启用 LLM 战术建议（可选）

需要一个 API Key，从 <https://open.bigmodel.cn> 申请。**只通过环境变量提供，代码与场景中不存密钥。**

```powershell
# 1) 彻底退出 Unity 与 Unity Hub —— 环境变量在进程启动时读取
Get-Process Unity -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process 'Unity Hub' -ErrorAction SilentlyContinue | Stop-Process -Force

# 2) 设置环境变量（可参考 .env.example）
$env:ARKCARD_LLM_API_KEY = '你的Key'

# 3) 从【同一个窗口】启动 Unity —— 不要用 Unity Hub 点，Hub 继承不到该变量
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe" `
    -projectPath "<本项目路径>"
```

之后进 Play Mode，先结束自己的回合（AI 在自己的回合才决策），Console 出现下面这行即表示通道正常：

```
[AI Hybrid] LLM=<策略文本>, confidence=0.90, action=EndTurn
```

场景中 `player2` 上的 `Enable LLM Strategy` 与 `Enable Ai Debug Log` 已保存为开启状态。
未配置 Key 时 LLM 通道自动跳过，AI 继续使用本地规划器，**不影响对局**。

### 第 3 档：重训 AI 模型（可选）

需要 Python ≥ 3.10、构建 ML-Agents 训练环境，并重跑数据生成与训练流程。
详见 `Docs/AI/Training.md` 与 `Docs/项目运行配置清单.md` 第 3 节。

> 重训前请核对编码维度：C# 与 Python 两侧当前均为 `2180 / 739`（策略输入 `2919`），
> 已对齐。校验命令：`python -m arkcard_ai.inspect_dataset <数据集目录>`。

---

## 二、目录结构

```
ArkCard-main/
├── Assets/                      # Unity 资源与脚本（核心）
│   ├── Scripts/
│   │   ├── AI/                  # AI 层
│   │   │   ├── ML/
│   │   │   │   ├── Agents/      # CardBattleAgent（ML-Agents Agent）
│   │   │   │   ├── Contracts/   # IAIPlanner / IPolicyValueProvider（替换点）
│   │   │   │   ├── Encoding/    # AIEncodingSchema / AIFeatureEncoder
│   │   │   │   ├── Inference/   # AIModelConfig / Barracuda 推理
│   │   │   │   └── Planning/    # NeuralMCTSPlanner / ResilientAIPlanner
│   │   │   ├── LLM/             # LLMStrategyClient + 接口适配层
│   │   │   ├── BattleStateSnapshot.cs    # 不可变局面快照
│   │   │   ├── BattleStateSimulator.cs   # 合法动作生成 + 规则推演
│   │   │   └── MCTSPlanner.cs            # 旧启发式 MCTS（降级用）
│   │   ├── Controller/          # PlayerController / AIController / CardController
│   │   ├── Manager/             # BattleManager / EffectManager / TargetManager 等
│   │   ├── Effects/             # 可配置卡牌效果（34 种）
│   │   ├── UI/                  # 卡面、费用、血量、回合、胜负面板
│   │   └── Editor/AITraining/   # 训练数据生成、竞技场、场景配置菜单
│   ├── Tests/
│   │   ├── EditMode/            # 约 120 项单元测试
│   │   └── PlayMode/            # 7 项对局冒烟测试
│   ├── AI/Configs/              # ML-Agents PPO 配置、模型配置
│   ├── Resources/               # 卡牌数据库、牌组、预制体
│   └── Scenes/BattleScene.unity # 主战斗场景
├── Packages/                    # Unity 包依赖（manifest + lock）
├── ProjectSettings/             # Unity 工程设置（含版本号）
├── Tools/AITraining/            # 自研训练链路（Python）
│   ├── arkcard_ai/              # 数据集读写、模型、训练、ONNX 导出
│   ├── configs/                 # 分轮次训练配置
│   └── tests/                   # 19 项 Python 测试
├── Docs/                        # 设计与流程文档（含配置清单、密钥管理、AI 说明）
├── new/                         # AI 改动的独立副本（见下）
├── .env.example                 # 环境变量模板（只含变量名，无真实密钥）
└── .gitignore
```

> **关于 `new/` 目录**：其中是 AI 相关改动的独立副本（奖励函数扩展、
> LLM 调用限频、`IAIStrategy`/`CommandInvoker`/`AICommandMapper`/`AdaptiveAIStrategy`
> 接口预留层）。这些改动**已同步部署到 `Assets/`**，`new/` 仅作为改动留档与回滚参照。
> `IAIStrategy` 等 4 个适配层文件属于**接口预留**，运行时未被调用。

---

## 三、密钥与安全

**API Key 绝不进仓库**，本项目从设计上遵循这一原则：

1. 代码中不硬编码 —— 只从环境变量 `ARKCARD_LLM_API_KEY` 读取
2. 场景与资源中不保存 —— `LLMStrategyClient` 由 `AIController.ResolveLLMClient()`
   在运行时自动挂载，Inspector 上不会残留密钥
3. 仓库中不放真实值 —— 只提交 `.env.example`（仅含变量名）

`.gitignore` 已排除 `.env`、`secrets.json`、根目录临时截图等可能含密钥的文件。

**上传前自检**（两条都应无输出）：

```powershell
# 1) 确认没有明文密钥
Get-ChildItem -Recurse -File |
  Where-Object { $_.Extension -notin '.png','.jpg','.dll','.pdb','.onnx','.pt','.gz','.zip','.exe' } |
  Select-String -Pattern 'sk-[0-9a-fA-F]{16,}' | Select-Object Path, LineNumber

# 2) 确认 .env 未被跟踪
git status --short | Select-String '\.env'
```

详细说明见 `Docs/环境变量与密钥管理.md`（含泄露后的处置流程）。

---

## 四、文档索引

| 文档 | 内容 |
| --- | --- |
| `Docs/项目运行配置清单.md` | **克隆后必读**：三档配置、常见问题、从零到可运行的完整命令 |
| `Docs/环境变量与密钥管理.md` | 密钥的三种设置方式、防止误提交、泄露后处置 |
| `Docs/AI/Training.md` | 自研 AlphaZero 风格训练链路的原理与流程 |
| `Docs/AI/HybridIntegration.md` | 运行时 AI 结构（规划器 + LLM + 降级） |
| `Docs/AI/MLAgentsSetup.md` | ML-Agents 场景配置步骤 |
| `Docs/AI/Baseline.md` | 神经 MCTS 与旧 MCTS 的对照基线参数 |
| `Docs/AI/ModelPromotion.md` | 候选模型竞技场晋级流程 |
| `Docs/AI/NewCardCompatibility.md` | 新增卡牌与编码 schema 的兼容规则 |
| `AGENTS.md` / `CLAUDE.md` | 开发约定与 Unity MCP 工作流 |

---

## 五、已知状态与缺口

| 项 | 状态 |
| --- | --- |
| 基础玩法（回合/费用/抽卡/随从攻击/法术选目标/墓地复活/胜负判定） | 已实现，可完整打完一局 |
| 右键撤销 | 部分实现：覆盖"选目标阶段 + 待打出卡牌" |
| 随从技能 | 机制齐全，但仅 1 张卡配置了启动技能；且 `castUsed` 目前只在召唤时重置 |
| ML-Agents 环境与训练 | 已搭建并真实跑通一轮 PPO，产出检查点与 ONNX |
| 神经推理（Barracuda + 神经 MCTS） | **未启用**：仓库内 ONNX 为 schema v1，运行时为 v2；`Assets/AI/Models/` 为空。运行时自动回退规则 MCTS |
| LLM 战术建议 | 已打通：调用时机（开局/每 N 回合/劣势）、置信度阈值、六类降级路径均可用 |
| 音效 | 音频资源已就位，但**尚未接线**到游戏事件 |
| 粒子特效 | 未实现，战斗反馈仅使用 PrimeTween 补间 |
| 自动化测试 | EditMode 约 120 项 + PlayMode 7 项 + Python 19 项，均通过 |
