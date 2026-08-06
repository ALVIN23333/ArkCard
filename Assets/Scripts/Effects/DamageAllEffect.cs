using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.DamageAll, "伤害所有角色")]
public sealed class DamageAllEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.DamageAll;
    public override string Label => "伤害所有角色";

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "伤害值", 0, true),
    };

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.DamageCharacters(source, EffectValues.GetValue(effect, 0), false);
        onComplete?.Invoke();
    }

    public override void Simulate(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> targets,
        Random random)
    {
        int damage = EffectValues.GetValue(effect, 0);
        foreach (PlayerStateSnapshot player in state.Players)
        {
            EffectSimulationResolver.DamagePlayer(state, source, player, damage);
        }

        EffectSimulationResolver.DamageFields(state, source, damage, false, random);
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0) * 2;
    }
}
