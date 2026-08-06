/// <summary>
/// 效果参数的结构化描述，供编辑器生成参数输入框与校验使用。
/// </summary>
public sealed class EffectValueParameter
{
    public EffectValueParameter(int index, string label, int defaultValue, bool required)
    {
        Index = index;
        Label = label;
        DefaultValue = defaultValue;
        Required = required;
    }

    public int Index { get; }
    public string Label { get; }
    public int DefaultValue { get; }
    public bool Required { get; }
}
