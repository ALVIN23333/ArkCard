# 测试编写指南

## 选择测试类型

- **EditMode 测试**：适合纯逻辑（模拟层、规则、数值）。不进入 Play 模式，毫秒级完成，可大量覆盖。
- **PlayMode 测试**：适合真实场景集成（场景加载、MonoBehaviour 生命周期、协程/动画驱动的完整对局）。会进入/退出播放模式，运行较慢。

## 文件与程序集要求

- EditMode 测试放在 `Assets/Tests/EditMode/`，PlayMode 测试放在 `Assets/Tests/PlayMode/`，直接使用现有程序集即可。
- 新建独立测试程序集时，`.asmdef` 必须满足：

```json
{
    "name": "你的测试程序集名",
    "references": [
        "ArkCard.Runtime"
    ],
    "optionalUnityReferences": [
        "TestAssemblies"
    ],
    "includePlatforms": [
        "Editor"
    ]
}
```

- PlayMode 程序集不需要 `includePlatforms: ["Editor"]`；EditMode 必须加。
- 测试程序集**不能引用 `Assembly-CSharp`**，游戏代码一律通过 `ArkCard.Runtime` 引用。

## 常用构造工具（SimulationTestHelpers）

`Assets/Tests/EditMode/SimulationTestHelpers.cs` 提供纯快照层的状态构造：

```csharp
// 双玩家基础状态（各 30 血、10 费）
BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();

// 创建随从卡（被动可传单个或列表）
state.GetPlayer(0).Field.Add(
    SimulationTestHelpers.CreateCard(1, 0, CardState.Field, 3, 4, PassiveType.Guard, true));

// 创建法术并附带效果
CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(50, 0,
    new CardEffectData { effectType = EffectType.Draw, effectValues = new[] { 1 } });
state.GetPlayer(0).Hand.Add(spell);

// 生成合法动作并执行（自动断言法术可打出）
BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);
```

常用方法：

| 方法 | 说明 |
| --- | --- |
| `CreateBaseState()` | 初始双玩家状态（P0 当前回合） |
| `CreateCard(id, owner, state, atk, hp, passive, canAttack, cardType)` | 创建快照卡 |
| `CreateCard(..., List<PassiveType>, ...)` | 多被动版本 |
| `CreateSpell(id, owner, params CardEffectData[])` | 创建带效果的法术 |
| `FindPlaySpellAction(state, spellId)` | 查找打出该法术的合法动作 |
| `PlaySpell(state, spellId, seed)` | 执行并返回结果状态 |

## 数值断言测试模式

原则：**断言精确数值，而不是“不崩”**。

```csharp
[Test]
public void Draw_MovesExactCardFromDeckToHand()
{
    BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
    CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(50, 0,
        new CardEffectData { effectType = EffectType.Draw, effectValues = new[] { 1 } });
    state.GetPlayer(0).Hand.Add(spell);
    state.GetPlayer(0).DeckRemaining.Add(
        SimulationTestHelpers.CreateCard(51, 0, CardState.Deck, 1, 1, PassiveType.None, false));

    BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

    Assert.AreEqual(1, result.GetPlayer(0).Hand.Count);
    Assert.AreEqual(0, result.GetPlayer(0).DeckRemaining.Count);
}
```

容易踩的坑：

- **快照卡默认满血**：想测治疗，先手动把 `card.Health` 改低再施放效果。
- **法术牌打完后进入墓地**：断言墓地数量时要把法术牌本身算进去。
- **条件在法术离手后才判定**：例如 `ThreeMoreHand` 在效果结算时手牌已少一张，想触发 then 分支需要起手 4 张。
- **随机性**：抽牌/弃牌用种子 `new Random(seed)`；只断言数量与状态，不断言抽到哪张。

## PlayMode 冒烟测试模式

参考 `Assets/Tests/PlayMode/BattleSmokeTests.cs`：

```csharp
[UnityTest]
public IEnumerator FullGame_AIAutoBattle_ReachesGameOver()
{
    SceneManager.LoadScene("BattleScene");      // 场景需在 Build Settings 中
    yield return null;

    // 轮询等待初始化（双方手牌 >= 5）
    // 开启加速：Time.timeScale = 3f; AnimeManager.Instant = true;
    // 启用主玩家托管：AutoPlayDriver.GetOrCreate().enabled = true;
    // 轮询直到 GM.Ins.BM.IsGameOver 或超时
}
```

要点：

- 使用 `Time.unscaledDeltaTime` 计时，避免受测试内 `timeScale` 影响。
- 测试内用 `Debug.Log` 输出里程碑（如 `[SMOKE] game over reached`），便于从外部日志确认进度。
- 不要用 `LogAssert.NoUnexpectedReceived()` 拦截全部日志——游戏自身会产生序列化警告（`CardEffectData` 递归结构）；**错误日志仍会自动导致测试失败**。
- `[TearDown]` 中恢复 `Time.timeScale = 1f`、`AnimeManager.Instant = false`，并销毁 `AutoPlayDriver`。

## 验证流程

改完代码后：

1. 通过 Unity MCP `request_recompile` + `get_compilation_errors` 确认无编译错误。
2. Test Runner 跑 `ArkCard.EditMode.Tests`，全绿后再跑 `ArkCard.PlayMode.Tests`。
3. 涉及运行时的改动，进 Play 用 GM 面板手工验证一遍（见 `GM-Debug-Panel.md`）。
