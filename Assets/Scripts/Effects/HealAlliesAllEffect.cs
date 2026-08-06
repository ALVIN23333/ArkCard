using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.healAlliesAll, "治疗全体友方")]
public sealed class HealAlliesAllEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.healAlliesAll;
    public override string Label => "治疗全体友方";

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "治疗值", 0, true),
    };

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.HealAllies(source, EffectValues.GetValue(effect, 0));
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

        int healValue = EffectValues.GetValue(effect, 0);
        SimulationEffectActions.HealPlayer(owner, healValue);
        foreach (CardStateSnapshot card in new List<CardStateSnapshot>(owner.Field))
        {
            SimulationEffectActions.HealCard(card, healValue);
        }
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0) * 1.5;
    }
}
