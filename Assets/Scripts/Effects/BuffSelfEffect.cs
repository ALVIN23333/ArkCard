using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.BuffSelf, "强化自身")]
public sealed class BuffSelfEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.BuffSelf;
    public override string Label => "强化自身";

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "攻击变化", 0, true),
        new EffectValueParameter(1, "生命变化", 0, true),
    };

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        source.AddStats(EffectValues.GetValue(effect, 0), EffectValues.GetValue(effect, 1));
        onComplete?.Invoke();
    }

    public override void Simulate(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> targets,
        Random random)
    {
        SimulationEffectActions.AddStats(source, EffectValues.GetValue(effect, 0), EffectValues.GetValue(effect, 1));
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0) * 2 + EffectValues.GetValue(effect, 1);
    }
}
