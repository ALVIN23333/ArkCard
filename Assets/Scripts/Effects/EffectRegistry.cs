using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// 统一效果注册表：所有效果的注册、元数据、执行与 AI 行为均通过它查询。
/// 注册方式：新建一个实现 ICardEffectDefinition 并标注 [CardEffect] 的类即可自动注册。
/// </summary>
public static class EffectRegistry
{
    private static Dictionary<EffectType, ICardEffectDefinition> definitions;
    private static IReadOnlyList<string> labels;
    private static IReadOnlyList<EffectType> enumOrder;
    private static bool initialized;

    private static readonly NullEffectDefinition NullDefinition = new();

    public static ICardEffectDefinition Get(EffectType effectType)
    {
        EnsureInitialized();
        return definitions.TryGetValue(effectType, out ICardEffectDefinition definition)
            ? definition
            : NullDefinition;
    }

    public static bool IsRegistered(EffectType effectType)
    {
        EnsureInitialized();
        return definitions.ContainsKey(effectType);
    }

    public static IEnumerable<ICardEffectDefinition> All
    {
        get
        {
            EnsureInitialized();
            return definitions.Values;
        }
    }

    /// <summary>
    /// 与 EffectType 枚举顺序一致的效果标签列表，供编辑器下拉框按 enumValueIndex 对齐使用。
    /// </summary>
    public static IReadOnlyList<string> GetLabels()
    {
        EnsureInitialized();
        if (labels != null)
        {
            return labels;
        }

        Array values = Enum.GetValues(typeof(EffectType));
        List<string> result = new(values.Length);
        foreach (EffectType effectType in values)
        {
            result.Add(Get(effectType).Label);
        }

        labels = result;
        return labels;
    }

    /// <summary>EffectType 枚举值按声明顺序排列，标签列表与之对齐。</summary>
    public static IReadOnlyList<EffectType> GetEnumOrder()
    {
        EnsureInitialized();
        if (enumOrder != null)
        {
            return enumOrder;
        }

        Array values = Enum.GetValues(typeof(EffectType));
        EffectType[] order = new EffectType[values.Length];
        int index = 0;
        foreach (EffectType effectType in values)
        {
            order[index++] = effectType;
        }

        enumOrder = order;
        return enumOrder;
    }

    /// <summary>枚举值在标签列表中的位置，供编辑器下拉框显示索引使用。</summary>
    public static int GetLabelIndex(EffectType effectType)
    {
        IReadOnlyList<EffectType> order = GetEnumOrder();
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == effectType)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>标签列表位置对应的枚举值，供编辑器下拉框写回使用。</summary>
    public static EffectType GetEffectTypeAt(int labelIndex)
    {
        IReadOnlyList<EffectType> order = GetEnumOrder();
        return labelIndex >= 0 && labelIndex < order.Count ? order[labelIndex] : EffectType.None;
    }

    public static bool TryGetMissingRequiredParameter(CardEffectData effect, out EffectValueParameter missingParameter)
    {
        missingParameter = null;
        if (effect == null)
        {
            return false;
        }

        ICardEffectDefinition definition = Get(effect.effectType);
        foreach (EffectValueParameter parameter in definition.Parameters)
        {
            if (!parameter.Required)
            {
                continue;
            }

            if (effect.effectValues == null || effect.effectValues.Length <= parameter.Index)
            {
                missingParameter = parameter;
                return true;
            }
        }

        return false;
    }

    private static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        BuildRegistry();
    }

    private static void BuildRegistry()
    {
        definitions = new Dictionary<EffectType, ICardEffectDefinition>();
        labels = null;
        enumOrder = null;

        Type[] types;
        try
        {
            types = typeof(ICardEffectDefinition).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types;
        }

        if (types != null)
        {
            foreach (Type type in types)
            {
                if (type == null
                    || type.IsAbstract
                    || type.IsInterface
                    || !typeof(ICardEffectDefinition).IsAssignableFrom(type))
                {
                    continue;
                }

                CardEffectAttribute attribute = type.GetCustomAttribute<CardEffectAttribute>(false);
                if (attribute == null)
                {
                    continue;
                }

                ICardEffectDefinition instance = (ICardEffectDefinition)Activator.CreateInstance(type);
                if (definitions.TryGetValue(attribute.EffectType, out ICardEffectDefinition existing))
                {
                    Debug.LogError(
                        $"EffectRegistry: duplicate effect registration for {attribute.EffectType} by {type.FullName}; "
                        + $"keeping {existing.GetType().FullName}.");
                    continue;
                }

                definitions.Add(attribute.EffectType, instance);
            }
        }

        initialized = true;
    }

    /// <summary>
    /// 未注册效果的兜底定义：执行与模拟均为空操作，供旧数据/未知枚举值安全降级。
    /// </summary>
    private sealed class NullEffectDefinition : ICardEffectDefinition
    {
        public EffectType EffectType => EffectType.None;
        public string Label => "未定义";
        public IReadOnlyList<EffectValueParameter> Parameters => Array.Empty<EffectValueParameter>();
        public bool IsTargeted => false;
        public TargetSelectionZone SelectionZone => TargetSelectionZone.Field;
        public int SuggestedArrayLength => 0;

        public int GetSelectionCount(CardEffectData effect) => 1;
        public int GetRuntimeSelectionCount(CardController source, CardEffectData effect) => 1;
        public int GetSimulationSelectionCount(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect) => 1;

        public List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
        {
            return new List<UnityEngine.Object>();
        }

        public List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
        {
            return new List<SimulatedTarget>();
        }

        public void ApplyRuntime(
            CardEffectContext context,
            CardController source,
            CardEffectData effect,
            List<UnityEngine.Object> targets,
            Action onComplete)
        {
            onComplete?.Invoke();
        }

        public void Simulate(
            BattleStateSnapshot state,
            CardStateSnapshot source,
            CardEffectData effect,
            List<SimulatedTarget> targets,
            Random random)
        {
        }

        public double ScoreSimulationTarget(
            BattleStateSnapshot state,
            CardStateSnapshot source,
            CardEffectData effect,
            SimulatedTarget target)
        {
            return double.MinValue;
        }

        public double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
        {
            return double.MinValue;
        }

        public double HeuristicScore(CardEffectData effect) => 0;
    }
}
