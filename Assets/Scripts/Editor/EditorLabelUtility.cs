using System.Collections.Generic;

internal static class EditorLabelUtility
{
    private static readonly List<string> CardTypeLabels = new() { "未定义", "随从", "法术" };
    private static readonly List<string> PassiveTypeLabels = new()
    {
        "无",
        "冲锋",
        "突袭",
        "嘲讽",
        "潜行",
        "横扫",
        "风怒",
        "圣盾",
        "吸血",
        "剧毒",
        "魔免",
    };
    private static readonly List<string> TriggerTypeLabels = new() { "无", "回合开始", "回合结束", "战吼", "亡语", "受伤", "施法" };
    private static readonly List<string> ConditionTypeLabels = new()
    {
        "无",
        "手牌不少于 3",
        "存在其他随从",
        "存在敌方随从",
        "存在友方随从",
        "有已死亡友方随从",
        "有已死亡敌方随从",
        "场上有空位",
        "具有非魔免友方随从",
        "具有非魔免敌方随从",
        "具有非魔免其他随从",
    };
    private static readonly List<string> CardAIRoleLabels = new() { "无", "节奏", "解场", "终结", "支援", "资源" };
    private static readonly List<string> AIPlayStyleLabels = new() { "默认", "进攻", "防守", "保留连携" };
    private static readonly List<string> AITargetPriorityLabels = new() { "默认", "敌方英雄", "高攻敌人", "低血敌人", "守卫优先", "虚弱友方", "强力友方" };
    public static List<string> GetCardTypeLabels() => CardTypeLabels;
    public static List<string> GetPassiveTypeLabels() => PassiveTypeLabels;
    public static List<string> GetTriggerTypeLabels() => TriggerTypeLabels;
    public static List<string> GetConditionTypeLabels() => ConditionTypeLabels;
    public static List<string> GetEffectTypeLabels() => new(EffectRegistry.GetLabels());
    public static List<string> GetCardAIRoleLabels() => CardAIRoleLabels;
    public static List<string> GetAIPlayStyleLabels() => AIPlayStyleLabels;
    public static List<string> GetAITargetPriorityLabels() => AITargetPriorityLabels;

    public static string GetCardTypeLabel(CardType cardType) => GetLabel(CardTypeLabels, (int)cardType);
    public static string GetPassiveTypeLabel(PassiveType passiveType) => GetLabel(PassiveTypeLabels, (int)passiveType);
    public static string GetTriggerTypeLabel(TriggerType triggerType) => GetLabel(TriggerTypeLabels, (int)triggerType);
    public static string GetConditionTypeLabel(ConditionType conditionType) => GetLabel(ConditionTypeLabels, (int)conditionType);
    public static string GetEffectTypeLabel(EffectType effectType) => GetLabel(GetEffectTypeLabels(), EffectRegistry.GetLabelIndex(effectType));
    public static string GetCardAIRoleLabel(CardAIRole role) => GetLabel(CardAIRoleLabels, (int)role);
    public static string GetAIPlayStyleLabel(AIPlayStyle style) => GetLabel(AIPlayStyleLabels, (int)style);
    public static string GetAITargetPriorityLabel(AITargetPriority priority) => GetLabel(AITargetPriorityLabels, (int)priority);

    private static string GetLabel(IReadOnlyList<string> labels, int index)
    {
        return index >= 0 && index < labels.Count ? labels[index] : "未定义";
    }
}
