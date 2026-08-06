# 新卡与编码兼容性

编码只读取游戏语义：卡牌类型、费用、攻防、运行状态、被动、触发器、条件、效果类型和效果数值。`aiBasePriority`、`aiRole`、`aiPlayStyle`、`aiTargetPriority`、`aiLethalBonus` 等旧启发式字段不得进入模型输入。

新增卡牌时按以下规则处理：

- 只组合现有 `EffectType`、`PassiveType`、`TriggerType` 和 `ConditionType`：schema 不变，旧模型可以零样本运行，但仍需加入自博弈数据并重新晋级。
- 新增上述任一枚举值或改变现有值的语义：提升 `AIEncodingSchema.Version`，同步调整维度常量、C#/Python 编码和黄金向量；旧模型必须被自动拒绝。
- 新增编码字段、改变槽位顺序、归一化范围或隐藏信息规则：提升 schema 版本。
- 新卡的规则模拟必须先有真实 `BattleManager` 与 `BattleStateSimulator` 的同种子一致性测试，再进入数据生成。
- 多目标效果必须枚举全部合法组合；任何策略偏好只能存在于旧 MCTS 或模型中，不能再次写回合法动作生成。

提交新卡前至少运行：完整合法动作枚举、目标组合、效果/被动/死亡/抽牌回归、隐藏信息不泄漏、编码维度与黄金向量、模型拒绝/回退测试。
