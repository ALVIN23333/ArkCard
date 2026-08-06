using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.DamageAllEnemy, "伤害所有敌方角色")]
public sealed class DamageAllEnemyEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.DamageAllEnemy;
    public override string Label => "伤害所有敌方角色";

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
        RuntimeEffectActions.DamageCharacters(source, EffectValues.GetValue(effect, 0), true);
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
        int damage = EffectValues.GetValue(effect, 0);
        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player != null && owner != null && player.PlayerIndex != owner.PlayerIndex)
            {
                EffectSimulationResolver.DamagePlayer(state, source, player, damage);
            }
        }

        EffectSimulationResolver.DamageFields(state, source, damage, true, random);
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0) * 7;
    }
}
