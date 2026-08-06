# 统一效果注册表（EffectRegistry）使用文档

## 1. 概述

`EffectRegistry` 是本项目的效果唯一注册与执行入口。一个效果的**编辑器元数据、参数 schema、运行时执行、AI 模拟、目标选择与评分、启发式评分**全部收敛到同一个定义类中，由注册表自动收集。

新增一个效果只需要两步：

1. 在 `GameEnum.cs` 的 `EffectType` 枚举**末尾追加**一个枚举值（不要插入中间，否则已有卡牌数据的枚举索引会错位）。
2. 在本目录新建一个定义类，标注 `[CardEffect]` 特性并继承 `CardEffectDefinitionBase`。

之后无需再改其他任何文件：编辑器下拉框、参数输入框、必填校验、运行时执行、AI 模拟与评分全部自动生效。

## 2. 目录与文件说明

| 文件 | 作用 |
| --- | --- |
| `EffectRegistry.cs` | 静态注册表：反射扫描、按 `EffectType` 建字典、标签列表生成、必填参数校验、未注册兜底定义 |
| `ICardEffectDefinition.cs` | 效果定义接口（所有效果必须实现的完整契约） |
| `CardEffectDefinitionBase.cs` | 抽象基类，提供非定向效果的默认行为，绝大多数效果只需覆写少量方法 |
| `CardEffectAttribute.cs` | `[CardEffect(EffectType, Label)]` 特性，注册表据此收集定义类 |
| `CardEffectContext.cs` | 效果执行上下文（取消/提交/回滚），由 EffectManager 创建并传入 `ApplyRuntime` |
| `EffectValueParameter.cs` | 参数描述（下标、标签、默认值、是否必填），编辑器据此生成输入框 |
| `EffectValues.cs` | `effectValues` 数组的安全读取工具 |
| `RuntimeEffectActions.cs` | 运行时共享原语（抽牌、增益、伤害、回手、复活等），定义类在这里操作真实对象 |
| `SimulationEffectActions.cs` | AI 模拟共享原语（纯数据快照版） |
| `EffectTargetingRules.cs` | 目标候选规则（潜行/魔免/排除源卡/墓园筛选）与评分工具，运行时与模拟共用 |
| `*Effect.cs` | 现有 21 个效果的定义类，每个文件对应一个 `EffectType` |

## 3. 核心概念

### 3.1 定向效果 vs 非定向效果

- **非定向**（`IsTargeted == false`）：如抽牌、全场伤害。框架会在执行前自动 `CommitEffect()`，然后调用 `ApplyRuntime(context, source, effect, null, onComplete)`；AI 模拟时传入的 `targets` 为 `null`。
- **定向**（`IsTargeted == true`）：如选择敌方造成伤害、复活友方。框架负责目标选择 UI / AI 自动选目标，解析完成后把目标列表传给 `ApplyRuntime`；模拟时由 `EffectSimulationResolver` 先解析目标再调用 `Simulate`。

### 3.2 运行时执行 vs AI 模拟

同一效果必须同时实现两套执行：

- `ApplyRuntime`：操作真实 `CardController` / `PlayerController`，通过 `RuntimeEffectActions` 完成。
- `Simulate`：操作纯数据快照 `BattleStateSnapshot`，通过 `SimulationEffectActions` 完成；涉及伤害/击杀时调用 `EffectSimulationResolver.DamageCard / DamagePlayer / KillCard / DamageFields`，以保持圣盾、吸血、剧毒、亡语等规则一致。

两套实现语义必须一致，否则 AI 会做出与实际游戏不符的决策。

### 3.3 注册与兜底

- 注册表懒初始化，首次访问时反射扫描 **`ArkCard.Runtime` 程序集**（即 `Assets/Scripts` 下的脚本）。因此效果定义类必须放在 Runtime 程序集内，不能放在 Editor 或 Test 程序集。
- 重复注册同一 `EffectType`：`Debug.LogError` 并保留先注册的定义。
- 未注册的枚举值：返回 No-op 的 `NullDefinition`（标签“未定义”，执行与模拟为空操作），不会崩溃；卡牌编辑器校验面板会给出“效果类型未注册”警告。

## 4. 接口成员速查

| 成员 | 说明 | 基类默认 |
| --- | --- | --- |
| `EffectType` | 关联的枚举值（abstract，必须覆写） | - |
| `Label` | 编辑器下拉框显示的中文名（abstract，必须覆写） | - |
| `Parameters` | `EffectValueParameter[]`，描述 `effectValues` 每个下标 | 空 |
| `IsTargeted` | 是否需要选择目标 | `false` |
| `SelectionZone` | 目标选择区域（`Field` / `Graveyard`），定向效果可覆写 | `Field` |
| `SuggestedArrayLength` | 编辑器据参数最大下标自动扩容 `effectValues` 的长度 | 由 `Parameters` 自动计算 |
| `GetSelectionCount(effect)` | 读取目标数量（`effectValues[SelectionCountIndex]`，缺失或 ≤0 时按 1） | 按 `SelectionCountIndex` 读取 |
| `GetRuntimeSelectionCount(source, effect)` | 运行时实际可选目标数，可覆写（如复活按空位封顶） | 同 `GetSelectionCount` |
| `GetSimulationSelectionCount(state, source, effect)` | 模拟时实际可选目标数 | 同 `GetSelectionCount` |
| `GetRuntimeCandidates(source, effect)` | 运行时候选目标列表（定向效果必须覆写） | 空 |
| `GetSimulationCandidates(state, source, effect)` | 模拟候选目标列表（定向效果必须覆写） | 空 |
| `ApplyRuntime(...)` | 运行时执行（abstract，必须覆写） | - |
| `Simulate(...)` | 模拟执行（abstract，必须覆写） | - |
| `ScoreSimulationTarget(...)` | 模拟目标评分（AI 选目标） | 目标威胁值 |
| `ScoreRuntimeTarget(...)` | 运行时目标评分（AI 自动选目标） | 目标威胁值 |
| `HeuristicScore(effect)` | 出牌启发式评分 | `effectValues[0]` |

`SelectionCountIndex` 的约定：绝大多数定向效果的目标数存在 `effectValues[1]`；`BuffEnemy` / `BuffAlly` 因 0/1 是攻击/生命变化，目标数在 `effectValues[2]`，对应类已覆写为 2。

## 5. 新增效果完整示例

### 5.1 非定向效果示例：随机摧毁一个敌方随从

先在 `GameEnum.cs` 末尾追加：

```csharp
public enum EffectType
{
    // ... 现有枚举 ...
    DestroyRandomEnemy, // 新效果：随机摧毁一个敌方随从
}
```

再新建 `Assets/Scripts/Effects/DestroyRandomEnemyEffect.cs`：

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.DestroyRandomEnemy, "随机摧毁敌方随从")]
public sealed class DestroyRandomEnemyEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.DestroyRandomEnemy;
    public override string Label => "随机摧毁敌方随从";

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "摧毁数量", 1, true),
    };

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        List<UnityEngine.Object> enemies = EffectTargetingRules.GetEnemyField(source);
        int count = Mathf.Min(EffectValues.GetValue(effect, 0), enemies.Count);
        for (int i = 0; i < count; i++)
        {
            int index = UnityEngine.Random.Range(0, enemies.Count);
            RuntimeEffectActions.DestroyTargets(new List<UnityEngine.Object> { enemies[index] });
            enemies.RemoveAt(index);
        }
        onComplete?.Invoke();
    }

    public override void Simulate(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> targets,
        Random random)
    {
        List<SimulatedTarget> enemies = EffectTargetingRules.GetEnemyField(state, source.OwnerIndex, false);
        int count = Math.Min(EffectValues.GetValue(effect, 0), enemies.Count);
        for (int i = 0; i < count && enemies.Count > 0; i++)
        {
            int index = random.Next(enemies.Count);
            EffectSimulationResolver.KillCard(state, state.FindCard(enemies[index].Id), random);
            enemies.RemoveAt(index);
        }
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0) * 12;
    }
}
```

### 5.2 定向效果示例：偷取敌方随从攻击力

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.StealEnemyAttack, "偷取敌方攻击")]
public sealed class StealEnemyAttackEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.StealEnemyAttack;
    public override string Label => "偷取敌方攻击";
    public override bool IsTargeted => true;

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "偷取攻击", 0, true),
        new EffectValueParameter(1, "目标数", 1, false),
    };

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        return EffectTargetingRules.GetEnemyField(source);
    }

    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        return EffectTargetingRules.GetEnemyField(state, source.OwnerIndex, false);
    }

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        int steal = EffectValues.GetValue(effect, 0);
        if (targets != null)
        {
            foreach (UnityEngine.Object target in targets)
            {
                if (target is CardController card)
                {
                    card.AddStats(-steal, 0);
                    source.AddStats(steal, 0);
                }
            }
        }
        onComplete?.Invoke();
    }

    public override void Simulate(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> targets,
        Random random)
    {
        int steal = EffectValues.GetValue(effect, 0);
        if (targets == null)
        {
            return;
        }
        foreach (SimulatedTarget target in targets)
        {
            CardStateSnapshot card = state.FindCard(target.Id);
            if (card != null)
            {
                SimulationEffectActions.AddStats(card, -steal, 0);
                SimulationEffectActions.AddStats(source, steal, 0);
            }
        }
    }

    public override double ScoreSimulationTarget(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        SimulatedTarget target)
    {
        CardStateSnapshot card = state.FindCard(target.Id);
        return card == null ? double.MinValue : EffectTargetingRules.GetSimulationThreat(card);
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        return target is CardController card && card.cardData != null
            ? EffectTargetingRules.GetRuntimeThreat(card)
            : double.MinValue;
    }
}
```

> 提示：新增枚举后必须重新编译（Unity 自动触发），注册表在首次访问时才会收集到新定义。

## 6. 现有 21 个效果一览

| EffectType | 编辑器标签 | effectValues 参数（下标:含义） | 定向 | 选择数下标 |
| --- | --- | --- | --- | --- |
| `Draw` | 友方抽牌 | 0:抽牌数 | 否 | - |
| `BuffSelf` | 强化自身 | 0:攻击变化, 1:生命变化 | 否 | - |
| `BuffAlliesAll` | 强化全体友方 | 0:攻击变化, 1:生命变化 | 否 | - |
| `BuffAllEnemies` | 强化全体敌方 | 0:攻击变化, 1:生命变化 | 否 | - |
| `healAlliesAll` | 治疗全体友方 | 0:治疗值 | 否 | - |
| `DamageAll` | 伤害所有角色 | 0:伤害值 | 否 | - |
| `DamageAllEnemy` | 伤害所有敌方角色 | 0:伤害值 | 否 | - |
| `AddCostMax` | 增加费用上限 | 0:费用上限变化 | 否 | - |
| `AddCost` | 增加当前费用 | 0:当前费用变化 | 否 | - |
| `AddBothCost` | 同时增加费用与上限 | 0:费用与上限变化 | 否 | - |
| `DisCard` | 友方随机弃牌 | 0:弃牌数 | 否 | - |
| `DealDamageToEnemy` | 选择敌方造成伤害 | 0:伤害值, 1:目标数 | 是 | 1 |
| `BuffEnemy` | 强化敌方 | 0:攻击变化, 1:生命变化, 2:目标数 | 是 | 2 |
| `SlienceEnemy` | 沉默敌方 | 1:目标数 | 是 | 1 |
| `DestoryEnemy` | 消灭敌方 | 1:目标数 | 是 | 1 |
| `BuffAlly` | 强化友方 | 0:攻击变化, 1:生命变化, 2:目标数 | 是 | 2 |
| `HealAlly` | 治疗友方 | 0:治疗值, 1:目标数 | 是 | 1 |
| `OtherBackHand` | 其他随从回手 | 1:目标数 | 是 | 1 |
| `AllyBackHand` | 友方回手 | 1:目标数 | 是 | 1 |
| `EnemyBackHand` | 敌方回手 | 1:目标数 | 是 | 1 |
| `ReviveAlly` | 复活友方 | 1:复活数量 | 是（墓园，按空位封顶） | 1 |

## 7. 共享工具类速查

### `RuntimeEffectActions`（运行时）

| 方法 | 作用 |
| --- | --- |
| `Draw(player, count)` | 抽牌 |
| `AddStats(card, atk, hp)` | 属性变化 |
| `BuffAllies / BuffEnemies(source, atk, hp)` | 全场友方/敌方增益 |
| `HealAllies(source, value)` | 治疗友方英雄与全部随从 |
| `AddCostAndMaxCost(player, value)` | 同时加费与上限 |
| `DiscardRandomCards(player, count)` | 随机弃牌 |
| `DamageCharacters(source, damage, enemyOnly)` | 伤害全部角色/敌方角色 |
| `DamageTargets / BuffTargets / HealTargets` | 对已选目标造成伤害/增益/治疗 |
| `SilenceTargets / DestroyTargets` | 沉默/消灭已选目标 |
| `ReturnTargetsToOwnerHand(targets)` | 目标回手 |
| `ReviveAllies(owner, targets)` | 复活友方随从（按空位封顶） |

### `SimulationEffectActions`（AI 模拟）

`DrawCards`、`Discard`、`AddStats`、`HealCard`、`HealPlayer`、`ReturnToHand`、`Revive`，语义与运行时版一一对应。

伤害/击杀原语仍在 `EffectSimulationResolver`：`DamageCard`、`DamagePlayer`、`KillCard`、`DamageFields`、`UpdateGameOver`。效果定义中涉及伤害时请调用这些方法，不要直接改快照数值，否则会绕过圣盾/吸血/剧毒/亡语等规则。

### `EffectTargetingRules`（候选与评分）

- 候选：`GetEnemyCharacters / GetEnemyField / GetAllyField / GetAllField / GetOtherField / GetAllyGraveyardMinions`，均有运行时（`CardController`）与模拟（`BattleStateSnapshot`）两套重载；已内置潜行、魔免、排除源卡等过滤。
- 评分：`GetSimulationThreat / GetSimulationAllyValue / GetRuntimeThreat / GetRuntimeAllyValue`、被动加成、增益量、`CountUsefulEffects`、`GetLethalBonus`，供 `Score*Target` 覆写时复用。

### `EffectValues`

`GetValue(effect, index)`：安全读取 `effectValues[index]`，越界/缺失返回 0。

## 8. 编辑器自动集成

- 效果下拉框标签来自 `EffectRegistry.GetLabels()`，与 `EffectType` 枚举顺序自动对齐，不再维护手写列表。
- 参数输入区由 `Parameters` 生成，切换效果类型时按 `SuggestedArrayLength` 自动扩容 `effectValues`。
- 校验面板（`CardValidationService`）自动检查：必填参数缺失（Error）、效果未注册（Warning）、法术卡触发器/被动等既有规则。
- 编辑器与运行时共用同一注册表，首次访问时自动扫描，无需手动登记。

## 9. 注意事项

1. **枚举只追加、不插入**：`EffectType` 的数值会序列化进 `ArkCardsDatabase.asset`，插入中间值会导致已有卡牌效果错位。
2. **运行时与模拟必须成对实现**：漏掉 `Simulate` 会导致 AI 对该效果“看不见”，漏掉 `ApplyRuntime` 会导致实际出牌无效。
3. **定向效果必须覆写候选**：只设 `IsTargeted = true` 而不提供 `GetRuntimeCandidates / GetSimulationCandidates`，效果会因为没有可选目标而无法发动。
4. **不要在 `ApplyRuntime` 里重复调用 `context.CommitEffect()`**：框架已在非定向效果执行前、定向目标确定后统一提交。
5. **选择数量约定**：目标数优先读取 `effectValues[SelectionCountIndex]`；带“目标数”参数的效果请沿用现有下标约定（默认 1，Buff 类为 2），保证编辑器与 AI 一致。
6. **需要按场面封顶的选数**（如复活按空位）：覆写 `GetRuntimeSelectionCount / GetSimulationSelectionCount`，参考 `ReviveAllyEffect`。
7. **定义类必须放在 Runtime 程序集**：`EffectRegistry` 只扫描 `ArkCard.Runtime`，放在 Editor/Test 程序集不会被注册。
8. **数值移植**：本次重构逐项照搬了原有数值与 AI 评分；新增效果请先在设计上确认数值，再同步写启发式评分 `HeuristicScore`。

## 10. 测试

- 注册表一致性测试位于 `Assets/Tests/EditMode/EffectRegistryTests.cs`：枚举全覆盖、标签对齐、定向真值表、必填参数校验、选择数、未注册兜底、启发式评分。
- 全卡库校验测试位于 `Assets/Tests/EditMode/DatabaseValidationTests.cs`：加载 `ArkCardsDatabase.asset`，断言无 Error 级校验问题。
- 运行时冒烟测试位于 `Assets/Tests/PlayMode/EffectRegistryPlayModeSmokeTests.cs`：真实施放抽牌法术，断言经注册表分发且手牌/牌库变化正确。
- 数值行为断言仍在 `Assets/Tests/EditMode/EffectValueAssertionTests.cs`，新增效果建议在其中补充对应的模拟数值用例。
