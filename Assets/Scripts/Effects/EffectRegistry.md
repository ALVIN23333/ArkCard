# 统一效果注册表与配置指南

## 概述

`EffectRegistry` 是卡牌效果的统一入口，负责编辑器元数据、目标选择、运行时执行、AI 模拟与启发式评分。效果定义位于 `Assets/Scripts/Effects`，必须同时实现真实对战和 `BattleStateSnapshot` 模拟语义。

统一效果 schema 版本为 `3`。卡牌编辑器加载旧卡库时会通过 `CardEffectMigrationService` 递归迁移顶层效果以及 `thenEffects`、`elseEffects`，并写入 `CardListSO.effectSchemaVersion`。迁移使用 Unity `SerializedObject`、Undo 和 `AssetDatabase`，禁止直接修改 `.asset` YAML；schema 已达到 3 后迁移幂等。

## CardEffectData

| 字段 | 作用 |
| --- | --- |
| `effectType` | 效果动作类型 |
| `targetSide` | `Friendly`、`Enemy`、`Both` |
| `targetMode` | `Self`、`All`、`Selected`、`Random` |
| `characterScope` | `Minions`、`Heroes`、`Characters` |
| `includeSource` | 友方/双方随从范围是否包含效果来源 |
| `effectValues` | 数值、数量、卡牌 ID、费用等整数参数 |

现有 `EffectType` 数值 `0-109` 永久保留。统一效果从 `110` 开始：

| EffectType | effectValues |
| --- | --- |
| `Damage` | `0:伤害值, 1:单位数` |
| `Heal` | `0:治疗值, 1:单位数` |
| `Destroy` | `0:单位数` |
| `Buff` | `0:攻击变化, 1:生命变化, 2:单位数` |
| `BackHand` | `0:单位数` |
| `Discard` | `0:弃牌数` |
| `Revive` | `0:复活数量` |
| `SummonMinion` | `0:随从卡 ID, 1:数量` |
| `SummonRandomCostMinion` | `0:指定费用, 1:数量` |
| `DrawCards` | `0:抽牌数量`，作用方由 `targetSide` 决定 |
| `Cost` | `0:当前费用增加, 1:费用上限增加`，两项均非负且至少一项大于 0 |
| `Silence` | `0:数量`（仅 `Selected`/`Random` 使用），只作用于随从 |

## 目标规则

- `Selected` 通过 `TargetManager` 选择目标；潜行敌人不可选，法术来源不能选择魔免随从。
- `Random` 和 `All` 不属于“选择”，因此可以影响潜行和魔免随从。
- 伤害和治疗支持随从、英雄或全部角色；消灭、强化和回手仅处理随从。
- 随机效果无放回抽取，单位数超过候选数量时按候选数量封顶。
- 随机治疗只从当前受伤角色中抽取，不用满血角色补足数量。
- `Both` 的伤害、治疗、强化和回手从双方合并候选中计算总单位数。

## 特殊效果语义

- 弃牌始终随机；`Both` 表示双方玩家各弃指定数量。
- 复活的阵营配置决定墓地候选。被选卡牌全部转移给效果来源玩家，并进入来源玩家场地；数量按来源玩家空位封顶。
- 指定卡召唤要求 ID 对应随从卡。
- 指定费用随机召唤从 `ArkCardsDatabase` 中所有同费用随从里独立随机。
- 召唤配置为 `Both` 时，友方和每个敌方玩家各召唤指定数量。
- 召唤与复活只初始化随从并加入场地，不触发 `TriggerType.Enter`。

## 编辑器菜单

普通效果全部直接显示在菜单根级，每个效果族仅有一个入口；不再按“无需指定/需要指定”分区。仅保留禁用的 `特殊效果（预留）/暂无可用效果` 占位子菜单。

伤害、治疗、消灭、强化和回手每个效果族只显示一个统一入口，通过目标配置切换阵营、方式和范围。旧枚举仍保留注册以读取未迁移资产，但不出现在新建菜单。

- 伤害、治疗支持自身、全体、指定和随机。
- 消灭、回手支持全体、指定和随机。
- 强化支持自身、全体、指定和随机。
- 复活保持指定选择；弃牌与召唤不选择角色目标，只配置作用方。

## 旧效果迁移

| 旧类型 | 新配置 |
| --- | --- |
| `DamageAll` / `DamageAllEnemy` / `DealDamageToEnemy` | `Damage` + 对应阵营/方式/角色范围 |
| `healAlliesAll` / `HealAlly` | `Heal` + 对应方式 |
| `DestoryEnemy` | `Destroy + Enemy + Selected` |
| `BuffSelf` / `BuffAlliesAll` / `BuffAllEnemies` / `BuffAlly` / `BuffEnemy` | `Buff` + 对应方式/阵营 |
| `OtherBackHand` / `AllyBackHand` / `EnemyBackHand` | `BackHand` + 对应阵营和来源包含规则 |
| `DisCard` | `Discard + Friendly` |
| `ReviveAlly` | `Revive + Friendly` |

### Schema 3 migration

`Draw` migrates to `DrawCards + Friendly` with count at `effectValues[0]`.
`AddCost`, `AddCostMax`, and `AddBothCost` migrate to `Cost` values `[old,0]`, `[0,old]`, and `[old,old]` respectively.
`SlienceEnemy` migrates to `Silence + Enemy + Selected`; its count is read from legacy `effectValues[1]`, defaulting to 1 when absent.

Draw uses only `targetSide`: Friendly draws for the caster, Enemy draws for every opposing player, and Both draws for both players. Cost always affects the caster, raises max cost first (capped by `GameConst.costMax`), then current cost. Silence supports `Self`, `All`, `Selected`, and `Random`; Self is valid only for a minion source, manual selection retains stealth and spell-immunity filters, while All/Random bypass those selection filters.

迁移必须幂等：schema 版本达到当前版本后不得重复改写数据。

## 新增效果要求

1. 在 `GameEnum.cs` 末尾追加显式数值，禁止改变现有数值。
2. 新建继承 `CardEffectDefinitionBase` 的定义并标注 `[CardEffect]`。
3. 若是否需要选择取决于配置，覆写 `RequiresTargetSelection(CardEffectData)`。
4. 同步实现 `ApplyRuntime` 与 `Simulate`，伤害/击杀使用 `EffectSimulationResolver`。
5. 在 `EffectEditorCatalog` 登记编辑器入口，并补充迁移与 EditMode 测试。

## 验证

- `EffectRegistryTests`：注册表、枚举和参数契约。
- `ConfigurableEffectTests`：统一效果目标、随机、复活和召唤规则。
- `DatabaseValidationTests`：迁移后卡库无 Error。
- `EffectRegistryPlayModeSmokeTests`：运行时分发冒烟测试。

外部代码编辑后应退出 Play Mode，执行重新编译，检查编译错误和 Console 错误，再运行 EditMode/PlayMode 测试并读回迁移后的 `effectSchemaVersion` 与效果字段。
