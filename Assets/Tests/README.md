# ArkCard 测试工具使用文档

本文档汇总本项目当前的自动化测试与调试工具，以及它们的运行方式。

## 目录结构

```
Assets/Tests/
├── README.md                    # 本文档（总览与运行方式）
├── GM-Debug-Panel.md            # GM 调试面板使用文档
├── Writing-Tests.md             # 测试编写指南
├── EditMode/                    # 编辑模式测试（纯逻辑，秒级完成）
│   ├── ArkCard.EditMode.Tests.asmdef
│   ├── SimulationTestHelpers.cs         # 测试状态构造工具
│   ├── AIPlannerMigrationTests.cs       # 从 AIPlannerSelfTest 迁移的 6 个回归用例
│   └── EffectValueAssertionTests.cs     # 35 个精确数值断言用例
└── PlayMode/                    # 播放模式测试（真实场景 + AI 托管整局）
    ├── ArkCard.PlayMode.Tests.asmdef
    └── BattleSmokeTests.cs      # 全流程冒烟测试
```

## 当前测试覆盖

- **EditMode（41 个用例，全部通过）**：
  - AI 规划层回归：守卫规则、MCTS 斩杀选择、目标选择、快照隔离与未知抽牌、全部效果类型可执行、单动作短路搜索。
  - 数值断言：全部 `EffectType` 的精确数值（抽牌/费用/弃牌、Buff/治疗上限、伤害与胜负判定、沉默/消灭/回手/复活、条件分支）。
  - 被动组合：吸血、剧毒、圣盾、风怒、潜行、横扫、守卫+潜行、入场能力派生。
- **PlayMode（1 个用例，通过）**：加载 BattleScene，双方 AI 托管（主玩家走 `AutoPlayDriver`，敌方走 `AIController`），跳过动画 + 3 倍速跑完整局直至胜负判定，无错误日志。

## 如何运行测试

### 方式一：Test Runner 窗口（推荐日常使用）

1. 打开菜单 `Window > General > Test Runner`。
2. 选择 `EditMode` 或 `PlayMode` 标签页。
3. 点击 `Run All`（可只勾选 `ArkCard.EditMode.Tests` / `ArkCard.PlayMode.Tests` 程序集）。

PlayMode 测试会自动进入/退出播放模式，不需要手动操作。

### 方式二：命令行（CI / 批量回归）

以本机编辑器路径为例（Unity 2022.3.62f3c1）：

```powershell
# EditMode 测试
& "D:\unityEditor\2022.3.62f3c1\Editor\Unity.exe" `
  -batchmode -nographics -projectPath "D:\unityXiangmu\ArkCard" `
  -runTests -testPlatform EditMode -testResults "Temp/editmode-results.xml" -quit

# PlayMode 测试
& "D:\unityEditor\2022.3.62f3c1\Editor\Unity.exe" `
  -batchmode -nographics -projectPath "D:\unityXiangmu\ArkCard" `
  -runTests -testPlatform PlayMode -testResults "Temp/playmode-results.xml" -quit
```

只跑某个测试类可加 `-testFilter "EffectValueAssertionTests"`。注意：运行前请关闭已打开的 Unity 编辑器（项目被占用时会失败）。

### 方式三：通过 Unity MCP（Codex 会话内）

MCP 服务位于 `http://127.0.0.1:9999`。在 `execute_code` 中用 `TestRunnerApi` 启动测试：

```csharp
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

var api = ScriptableObject.CreateInstance<TestRunnerApi>();
api.Execute(new ExecutionSettings(new Filter
{
    testMode = TestMode.EditMode,               // 或 TestMode.PlayMode
    assemblyNames = new[] { "ArkCard.EditMode.Tests" },
}));
```

测试结果是异步回调，可通过注册 `ICallbacks` 输出到 Console 或文件后再读取。

## 测试程序集结构（重要）

Unity 测试程序集**不能引用预定义程序集 `Assembly-CSharp`**（官方限制），因此游戏代码已拆分为自定义程序集：

| 程序集 | 位置 | 内容 |
| --- | --- | --- |
| `ArkCard.Runtime` | `Assets/Scripts/ArkCard.Runtime.asmdef` | 全部游戏运行时代码（AI/Controller/Data/Manager/Others/SO/UI） |
| `ArkCard.Editor` | `Assets/Scripts/Editor/ArkCard.Editor.asmdef` | 编辑器工具（卡牌数据库编辑器等） |
| `ArkCard.EditMode.Tests` | `Assets/Tests/EditMode/` | EditMode 测试 |
| `ArkCard.PlayMode.Tests` | `Assets/Tests/PlayMode/` | PlayMode 测试 |

测试程序集通过 `references: ["ArkCard.Runtime"]` 引用游戏代码。新增测试文件时请放在已有测试程序集目录下，或按 `Writing-Tests.md` 的说明新建程序集。

## 相关调试工具

- **GM 调试面板**（运行时悬浮面板 + F1–F5 快捷键）：设置生命/费用、发指定卡牌、填场/清场、结束回合、时间倍率、跳过动画、AI 托管。详见 [GM-Debug-Panel.md](GM-Debug-Panel.md)。
- **AutoPlayDriver**：主玩家 AI 托管组件，冒烟测试与 GM 面板共用。
- **AnimeManager.Instant**：全局瞬时动画开关，测试与面板共用。

## 常见问题

- **PlayMode 测试跑完控制台出现 `Serialization depth limit 10 exceeded` 警告**：这是 `CardEffectData` 递归结构（then/else 分支）在序列化卡牌时的已知警告，不影响测试结果，冒烟测试已不再用 `LogAssert.NoUnexpectedReceived()` 拦截警告（错误日志仍会自动导致测试失败）。
- **测试找不到游戏类型**：检查新测试文件是否位于带 `ArkCard.Runtime` 引用的测试程序集目录下。
- **F1–F5 快捷键无效**：快捷键依赖 Game 视图获得键盘焦点，点击 Game 视图后再按。
- **重开场景后 GM 工具还能用吗**：能。GM 面板与 `AutoPlayDriver` 都是 `DontDestroyOnLoad`，点击 WinPanel 的“重新开始”重载场景后保持可用，无需重新进入 Play。
