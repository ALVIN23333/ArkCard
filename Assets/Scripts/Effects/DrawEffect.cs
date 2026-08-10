using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.Draw, "友方抽牌")]
public sealed class DrawEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.Draw;
    public override string Label => "友方抽牌";

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "抽牌数", 0, true),
    };

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.Draw(source.player, EffectValues.GetValue(effect, 0), onComplete);
    }

    public override void Simulate(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> targets,
        Random random)
    {
        PlayerStateSnapshot owner = state.GetPlayer(source.OwnerIndex);
        SimulationEffectActions.DrawCards(owner, EffectValues.GetValue(effect, 0), random);
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0) * 5;
    }
}
