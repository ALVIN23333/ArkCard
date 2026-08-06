using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.AddCostMax, "增加费用上限")]
public sealed class AddCostMaxEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.AddCostMax;
    public override string Label => "增加费用上限";

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "费用上限变化", 0, true),
    };

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        source.player.AddMaxCost(EffectValues.GetValue(effect, 0));
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
            owner.MaxCost = Math.Min(GameConst.costMax, owner.MaxCost + Math.Max(0, EffectValues.GetValue(effect, 0)));
        }
    }
}
