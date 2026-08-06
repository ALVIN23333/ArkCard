/// <summary>
/// 效果参数数组的通用读取工具。
/// </summary>
public static class EffectValues
{
    public static int GetValue(CardEffectData effect, int index)
    {
        if (effect == null || effect.effectValues == null || index < 0 || index >= effect.effectValues.Length)
        {
            return 0;
        }

        return effect.effectValues[index];
    }
}
