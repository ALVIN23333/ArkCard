using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.BuffAllEnemies, "强化全体敌方")]
public sealed class BuffAllEnemiesEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.BuffAllEnemies;
    public override string Label => "强化全体敌方";

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
        RuntimeEffectActions.BuffEnemies(source, EffectValues.GetValue(effect, 0), EffectValues.GetValue(effect, 1));
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

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player == null || player.PlayerIndex == owner.PlayerIndex)
            {
                continue;
            }

            foreach (CardStateSnapshot card in new List<CardStateSnapshot>(player.Field))
            {
                SimulationEffectActions.AddStats(card, EffectValues.GetValue(effect, 0), EffectValues.GetValue(effect, 1));
            }
        }
    }
}
