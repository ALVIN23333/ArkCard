using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.DisCard, "友方随机弃牌")]
public sealed class DiscardEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.DisCard;
    public override string Label => "友方随机弃牌";

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "弃牌数", 0, true),
    };

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.DiscardRandomCards(source.player, EffectValues.GetValue(effect, 0));
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
        SimulationEffectActions.Discard(owner, EffectValues.GetValue(effect, 0), random);
    }
}
