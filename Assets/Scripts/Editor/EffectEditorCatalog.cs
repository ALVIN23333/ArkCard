using System.Collections.Generic;

internal enum EffectEditorSection
{
    None,
    Special,
}

internal sealed class EffectEditorOption
{
    public EffectEditorOption(string label, EffectEditorSection section, EffectType effectType, EffectTargetMode targetMode)
    {
        Label = label;
        Section = section;
        EffectType = effectType;
        TargetMode = targetMode;
    }

    public string Label { get; }
    public EffectEditorSection Section { get; }
    public EffectType EffectType { get; }
    public EffectTargetMode TargetMode { get; }
}

internal static class EffectEditorCatalog
{
    private static readonly IReadOnlyList<EffectEditorOption> Options = new[]
    {
        new EffectEditorOption("未定义", EffectEditorSection.None, EffectType.None, EffectTargetMode.All),

        new EffectEditorOption("抽牌", EffectEditorSection.None, EffectType.DrawCards, EffectTargetMode.All),
        new EffectEditorOption("伤害", EffectEditorSection.None, EffectType.Damage, EffectTargetMode.Random),
        new EffectEditorOption("治疗", EffectEditorSection.None, EffectType.Heal, EffectTargetMode.Random),
        new EffectEditorOption("消灭", EffectEditorSection.None, EffectType.Destroy, EffectTargetMode.Random),
        new EffectEditorOption("强化", EffectEditorSection.None, EffectType.Buff, EffectTargetMode.Selected),
        new EffectEditorOption("回手", EffectEditorSection.None, EffectType.BackHand, EffectTargetMode.Selected),
        new EffectEditorOption("沉默", EffectEditorSection.None, EffectType.Silence, EffectTargetMode.Selected),
        new EffectEditorOption("费用", EffectEditorSection.None, EffectType.Cost, EffectTargetMode.All),
        new EffectEditorOption("弃牌", EffectEditorSection.None, EffectType.Discard, EffectTargetMode.Random),
        new EffectEditorOption("复活", EffectEditorSection.None, EffectType.Revive, EffectTargetMode.Selected),
        new EffectEditorOption("召唤指定随从", EffectEditorSection.None, EffectType.SummonMinion, EffectTargetMode.All),
        new EffectEditorOption("召唤指定费用随机随从", EffectEditorSection.None, EffectType.SummonRandomCostMinion, EffectTargetMode.Random),
    };

    public static IReadOnlyList<EffectEditorOption> GetOptions() => Options;

    public static string GetDisplayLabel(EffectType type, EffectTargetMode mode)
    {
        foreach (EffectEditorOption option in Options)
        {
            if (option.EffectType == type && option.TargetMode == mode)
            {
                return option.Label;
            }
        }
        return EffectRegistry.Get(type).Label;
    }

    public static IReadOnlyList<EffectTargetMode> GetModes(EffectType type)
    {
        return type switch
        {
            EffectType.Damage => new[] { EffectTargetMode.Self, EffectTargetMode.All, EffectTargetMode.Selected, EffectTargetMode.Random },
            EffectType.Heal => new[] { EffectTargetMode.Self, EffectTargetMode.All, EffectTargetMode.Selected, EffectTargetMode.Random },
            EffectType.Destroy => new[] { EffectTargetMode.All, EffectTargetMode.Selected, EffectTargetMode.Random },
            EffectType.Buff => new[] { EffectTargetMode.Self, EffectTargetMode.All, EffectTargetMode.Selected, EffectTargetMode.Random },
            EffectType.BackHand => new[] { EffectTargetMode.All, EffectTargetMode.Selected, EffectTargetMode.Random },
            EffectType.Silence => new[] { EffectTargetMode.Self, EffectTargetMode.All, EffectTargetMode.Selected, EffectTargetMode.Random },
            EffectType.Revive => new[] { EffectTargetMode.Selected },
            _ => new[] { EffectTargetMode.All },
        };
    }

    public static bool UsesTargetSide(EffectType type)
    {
        return type is EffectType.Damage or EffectType.Heal or EffectType.Destroy or EffectType.Buff
            or EffectType.BackHand or EffectType.Discard or EffectType.Revive
            or EffectType.SummonMinion or EffectType.SummonRandomCostMinion
            or EffectType.DrawCards or EffectType.Silence;
    }

    public static bool UsesCharacterScope(EffectType type) => type is EffectType.Damage or EffectType.Heal;

    public static bool UsesIncludeSource(EffectType type, EffectTargetMode mode)
    {
        return mode != EffectTargetMode.Self
            && type is EffectType.Damage or EffectType.Heal or EffectType.Destroy or EffectType.Buff or EffectType.BackHand;
    }

    public static bool HasTargetConfiguration(EffectType type) => IsUnified(type) && type != EffectType.Cost;

    public static bool IsUnified(EffectType type) => type >= EffectType.Damage && type <= EffectType.Silence;

    public static string GetSectionLabel(EffectEditorSection section)
    {
        return section switch
        {
            EffectEditorSection.Special => "特殊效果（预留）",
            _ => string.Empty,
        };
    }
}
