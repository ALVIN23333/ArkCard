# ArkCard AI v1 配置使用说明

本文说明当前 AI v1 中可配置属性的实际用途、推荐取值和配置方法。内容以当前代码实现为准。

## 1. 配置入口

卡牌 AI 属性保存在 `CardData` 中，可通过 Unity 菜单打开编辑器：

1. 打开 `Tools > ArkCards > Card Editor`。
2. 在左侧选择需要配置的卡牌。
3. 在右侧展开 `AI 配置`。
4. 修改属性后点击编辑器中的保存按钮。

新建卡牌时所有 AI 属性使用默认值；复制卡牌时会完整复制原卡的 AI 属性。旧卡没有显式配置时也会按默认值运行。

## 2. 配置原则

- AI 属性是软偏好，不是强制规则。MCTS 仍会根据费用、合法目标、场面收益和后续模拟选择动作。
- 不要用很大的 `aiBasePriority` 强行修复效果数据或合法性配置错误。卡牌无法使用时，应先检查费用、触发器、条件和目标范围。
- 一般只需配置有明确战术用途的卡牌。普通白板随从可以全部保持默认值。
- 同一局面可能因未知牌库抽样产生少量差异，但整体偏好应保持一致。

## 3. CardData AI 属性

### 3.1 `aiRole`：卡牌定位

该属性在“从手牌打出卡牌”的轻量启发式评分中提供固定加成，用于 MCTS 扩展顺序和 rollout 候选排序。

| 值 | 编辑器显示 | 当前加成 | 推荐用途 |
| --- | --- | ---: | --- |
| `None` | 无 | 0 | 白板卡、无需额外偏好的卡 |
| `Tempo` | 节奏 | +3 | 低费随从、抢节奏卡 |
| `Removal` | 解场 | +4 | 单体伤害、沉默、消灭、回手 |
| `Finisher` | 终结 | +5 | 直伤、冲锋、斩杀组件 |
| `Support` | 支援 | 0 | 治疗、增益等语义标记；当前没有额外数值加成 |
| `Value` | 资源 | +2 | 抽牌、复活、持续资源卡 |

注意：

- 定位加成只直接作用于“打出手牌”评分。
- 场上技能 `UseFieldCast` 当前不会获得 `aiRole` 加成。
- `Finisher` 不会自动获得斩杀目标加成，还需要配置 `aiLethalBonus`。

### 3.2 `aiPlayStyle`：打法偏好

| 值 | 编辑器显示 | 当前行为 |
| --- | --- | --- |
| `Default` | 默认 | 不附加额外规则 |
| `Aggressive` | 进攻 | 当前为预留值，尚未改变评分或 rollout |
| `Defensive` | 防守 | 当前为预留值，尚未改变评分或 rollout |
| `ComboReserve` | 保留连携 | 当前费用低于 `aiComboReserveThreshold` 时，打出该卡的启发式评分减 25 |

`ComboReserve` 适合需要等待费用或组合条件的卡牌。它只是降低提前打出的概率，不会禁止出牌；如果当前局面存在斩杀或高收益路径，MCTS 仍可能选择该卡。

### 3.3 `aiTargetPriority`：目标偏好

该属性在目标基础评分之上增加偏好。它会影响模拟目标排序，也会用于 AI 真实执行阶段的自动目标选择。

| 值 | 编辑器显示 | 额外评分 |
| --- | --- | --- |
| `Default` | 默认 | 仅使用效果类型自带的目标评分 |
| `EnemyHero` | 敌方英雄 | 敌方英雄目标 +100 |
| `HighAttackEnemy` | 高攻敌人 | 敌方随从额外 `3 × 攻击力` |
| `LowHealthEnemy` | 低血敌人 | 敌方随从额外 `max(0, 12 - 当前生命)` |
| `GuardFirst` | 守卫优先 | 未沉默的敌方守卫额外 +20 |
| `WeakAlly` | 虚弱友方 | 友方随从额外 `2 × 已损失生命` |
| `StrongAlly` | 强力友方 | 按攻击、最大生命、被动和效果数量增加友方价值分 |

目标偏好不会改变效果允许的目标集合。例如，治疗效果不能因为选择了 `EnemyHero` 而治疗敌方英雄。

效果类型本身已有基础策略：

- 指向伤害优先斩杀英雄、恰好击杀或处理高威胁随从。
- 沉默和消灭优先高威胁、强被动或高增益目标。
- 回手优先高费用、高增益、高威胁目标。
- 治疗优先受伤较重且价值较高的友方。
- 增益优先高价值、可立即攻击的友方。
- 复活优先高攻击、高生命、强被动和效果较多的随从。

### 3.4 `aiBasePriority`：基础优先级

该值直接加到以下动作的轻量启发式评分中：

- 从手牌打出该卡。
- 使用该随从的场上技能。

它主要影响 MCTS 的扩展顺序和 rollout 候选排序，不直接替代 MCTS 最终决策。

推荐范围：

| 数值 | 用途 |
| ---: | --- |
| `-10` 到 `-4` | 明显需要保留、容易空放或具有较大副作用 |
| `-3` 到 `3` | 普通卡牌，通常使用 0 |
| `4` 到 `10` | 希望优先尝试的核心节奏或解场卡 |
| `11` 到 `20` | 极强偏好，仅用于行为验证后确认需要的卡 |

超出 `[-20, 20]` 不会被程序截断，但卡牌校验面板会给出警告。

### 3.5 `aiComboReserveThreshold`：连携保留阈值

仅当 `aiPlayStyle == ComboReserve` 时生效。

判断规则：

```text
当前可用费用 < aiComboReserveThreshold
```

条件成立时，打出该卡的启发式评分减 25。

示例：阈值配置为 6，AI 当前只有 4 费时会明显降低尝试该卡的优先级；达到 6 费后不再应用该扣分。

`ComboReserve` 配合阈值 0 没有实际保留效果，校验面板会给出提示。

### 3.6 `aiLethalBonus`：斩杀加成

当该卡的指向伤害能够直接击杀敌方英雄时，此值会加入英雄目标评分。它主要用于让终结牌在多个合法目标之间更坚定地选择英雄。

推荐值：

| 数值 | 用途 |
| ---: | --- |
| `0` | 普通卡牌，不提供额外斩杀偏好 |
| `5` 到 `10` | 具备一定终结能力 |
| `11` 到 `20` | 明确的终结牌 |
| `20` 以上 | 强烈斩杀倾向，需通过调试日志验证 |

注意：

- 当前只在指向伤害选择敌方英雄且可直接击杀时使用。
- 它不会增加实际伤害，也不会让非法目标变为合法目标。
- `Finisher` 且加成为 0 时，校验面板会给出提示。
- 非 `Finisher` 卡牌的加成高于 10 时，校验面板会提示确认配置意图。

## 4. 常用配置模板

以下数值是起始建议，应结合调试日志和实际对局调整。

### 普通节奏随从

```text
aiRole = Tempo
aiPlayStyle = Default
aiTargetPriority = Default
aiBasePriority = 2
aiComboReserveThreshold = 0
aiLethalBonus = 0
```

### 单体解场法术

```text
aiRole = Removal
aiPlayStyle = Default
aiTargetPriority = HighAttackEnemy
aiBasePriority = 4
aiComboReserveThreshold = 0
aiLethalBonus = 0
```

如果法术更适合补刀，可将目标偏好改为 `LowHealthEnemy`；需要优先处理守卫时使用 `GuardFirst`。

### 直伤终结牌

```text
aiRole = Finisher
aiPlayStyle = Default
aiTargetPriority = EnemyHero
aiBasePriority = 5
aiComboReserveThreshold = 0
aiLethalBonus = 15
```

### 治疗卡

```text
aiRole = Support
aiPlayStyle = Default
aiTargetPriority = WeakAlly
aiBasePriority = 0
aiComboReserveThreshold = 0
aiLethalBonus = 0
```

### 关键增益卡

```text
aiRole = Support
aiPlayStyle = ComboReserve
aiTargetPriority = StrongAlly
aiBasePriority = 2
aiComboReserveThreshold = 5
aiLethalBonus = 0
```

### 抽牌或复活资源卡

```text
aiRole = Value
aiPlayStyle = Default
aiTargetPriority = StrongAlly
aiBasePriority = 1
aiComboReserveThreshold = 0
aiLethalBonus = 0
```

复活卡使用 `StrongAlly` 时，会更偏向基础属性、被动和效果更强的墓地随从。

## 5. AIController 搜索配置

`AIController` 组件还提供以下运行参数：

| 属性 | 默认值 | 作用 |
| --- | ---: | --- |
| `actionInterval` | 1.5 秒 | 两次真实 AI 动作之间的等待时间 |
| `searchIterations` | 300 | 单次 MCTS 最大迭代次数，运行时限制为 200 到 500 |
| `searchTimeBudgetMs` | 35 ms | 单次搜索软时间预算，迭代数或时间先到即停止 |
| `explorationConstant` | 1.4 | UCT 探索常数；越高越愿意尝试访问较少的动作 |
| `rolloutActionLimit` | 4 | 单次 rollout 最多模拟的连续动作数 |
| `enableAIDebugLog` | false | 是否输出每次决策的 MCTS 调试日志 |

rollout 每一步会从启发式评分最高的前 3 个动作中进行软偏好抽样；该数量当前固定在代码中，不在 Inspector 暴露。

建议：

- 正常对局优先保持默认值。
- 搜索耗时偏高时，先降低 `searchTimeBudgetMs`；`searchIterations` 无法低于 200。
- AI 行为过于保守或激进时，应优先调整卡牌元数据和效果配置，而不是直接修改探索常数。
- `actionInterval` 应覆盖主要动画和效果结算时间，不建议为追求速度设置得过低。

## 6. 调试与验证

开启 `enableAIDebugLog` 后，每次决策会输出：

- 当前根状态摘要。
- 合法动作数量。
- 实际迭代次数和耗时。
- 根动作的访问次数、平均价值和先验启发式分数。
- 最终选中的动作及目标。

分析日志时重点关注：

1. 合法动作中是否包含预期动作。若不存在，先检查费用、卡牌状态、条件、场地容量和目标范围。
2. 预期动作的 `prior` 是否合理。异常时检查 `aiBasePriority`、定位、效果参数和目标偏好。
3. 预期动作的访问次数是否明显偏低。可能是模拟后的场面收益较差，而不是配置未生效。
4. 最终动作是否与访问次数最高的动作一致。通常应一致；时间预算过低时可能只完成少量扩展。

可通过 Unity 菜单 `Tools > ArkCards > Run AI v1 Self Tests` 运行 AI 纯逻辑自测。

## 7. 常见问题

### 配置了 `Aggressive` 或 `Defensive`，行为没有变化

这是当前 v1 的实现边界。这两个值已经完成数据和编辑器接入，但尚未加入启发式评分或 rollout policy。需要立即影响行为时，请使用 `aiBasePriority`、`aiTargetPriority` 或 `ComboReserve`。

### `Support` 为什么没有更高优先级

当前 `Support` 主要作为语义标签，不提供定位加成。治疗和增益仍会由效果基础评分、目标价值和 MCTS 局面评估决定。需要额外提高尝试顺序时，可设置小幅正数 `aiBasePriority`。

### 设置很高的基础优先级后，AI 仍不出牌

基础优先级不会绕过合法性检查。请检查：

- 当前费用是否足够。
- 随从区是否已满。
- 卡牌条件是否满足。
- 卡牌类型、触发器和效果参数是否正确。
- AI 是否处于自己的回合，且效果和目标选择是否已经结算完成。

### 设置 `EnemyHero` 后，AI 为什么仍然解场

目标偏好只在该效果允许的候选目标中生效，而且 MCTS 会比较整个动作后的局面价值。敌方守卫可能阻止普通攻击打脸；非指向伤害动作也不会使用该目标偏好。

### 相同局面为什么偶尔选择不同动作

AI 对未知牌库使用剩余牌集合随机抽样，不读取真实牌序。少量差异属于预期行为；如果选择完全失去稳定性，应开启调试日志检查访问次数和时间预算。
