using System;

/// <summary>
/// 标记一个效果定义类，注册表通过该特性自动收集实现。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CardEffectAttribute : Attribute
{
    public EffectType EffectType { get; }
    public string Label { get; }

    public CardEffectAttribute(EffectType effectType, string label)
    {
        EffectType = effectType;
        Label = label;
    }
}
