using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.AddCost, "增加当前费用")]
public sealed class AddCostEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.AddCost;
    public override string Label => "增加当前费用";

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "当前费用变化", 0, true),
    };

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        source.player.AddCost(EffectValues.GetValue(effect, 0));
        onComplete?.Invoke();
    }

    public override void Simulate(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> targets,
        Random random)
    {
        PlayerStateSnapshot owner = state.GetPlayer(source.OwnerIndex);
        if (owner != null)
        {
            owner.Cost += Math.Max(0, EffectValues.GetValue(effect, 0));
        }
    }
}
