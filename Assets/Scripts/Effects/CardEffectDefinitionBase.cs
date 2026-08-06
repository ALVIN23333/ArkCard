using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// 效果定义基类：提供非定向效果的默认行为，定向效果按需覆写。
/// </summary>
public abstract class CardEffectDefinitionBase : ICardEffectDefinition
{
    public abstract EffectType EffectType { get; }
    public abstract string Label { get; }

    public virtual IReadOnlyList<EffectValueParameter> Parameters => Array.Empty<EffectValueParameter>();
    public virtual bool IsTargeted => false;
    public virtual TargetSelectionZone SelectionZone => TargetSelectionZone.Field;

    public virtual int SuggestedArrayLength
    {
        get
        {
            int maxIndex = -1;
            foreach (EffectValueParameter parameter in Parameters)
            {
                if (parameter.Index > maxIndex)
                {
                    maxIndex = parameter.Index;
                }
            }

            return maxIndex + 1;
        }
    }

    /// <summary>effectValues 中保存目标数量的参数下标，默认 1。</summary>
    public virtual int SelectionCountIndex => 1;

    public virtual int GetSelectionCount(CardEffectData effect)
    {
        int index = SelectionCountIndex;
        if (effect == null
            || effect.effectValues == null
            || index < 0
            || index >= effect.effectValues.Length
            || effect.effectValues[index] <= 0)
        {
            return 1;
        }

        return effect.effectValues[index];
    }

    public virtual int GetRuntimeSelectionCount(CardController source, CardEffectData effect)
    {
        return GetSelectionCount(effect);
    }

    public virtual int GetSimulationSelectionCount(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        return GetSelectionCount(effect);
    }

    public virtual List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        return new List<UnityEngine.Object>();
    }

    public virtual List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        return new List<SimulatedTarget>();
    }

    public abstract void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete);

    public abstract void Simulate(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> targets,
        Random random);

    public virtual double ScoreSimulationTarget(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        SimulatedTarget target)
    {
        CardStateSnapshot card = state != null ? state.FindCard(target.Id) : null;
        return EffectTargetingRules.GetSimulationThreat(card);
    }

    public virtual double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        return EffectTargetingRules.GetRuntimeThreat(target as CardController);
    }

    public virtual double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0);
    }
}
