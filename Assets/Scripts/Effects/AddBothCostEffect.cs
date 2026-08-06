using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.AddBothCost, "同时增加费用与上限")]
public sealed class AddBothCostEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.AddBothCost;
    public override string Label => "同时增加费用与上限";

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "费用与上限变化", 0, true),
    };

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.AddCostAndMaxCost(source.player, EffectValues.GetValue(effect, 0));
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
        if (owner == null)
        {
            return;
        }

        int amount = Math.Max(0, EffectValues.GetValue(effect, 0));
        owner.MaxCost = Math.Min(GameConst.costMax, owner.MaxCost + amount);
        owner.Cost += amount;
    }
}
