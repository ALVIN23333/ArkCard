using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.HealAlly, "治疗友方")]
public sealed class HealAllyEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.HealAlly;
    public override string Label => "治疗友方";
    public override bool IsTargeted => true;

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "治疗值", 0, true),
        new EffectValueParameter(1, "目标数", 1, false),
    };

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        return EffectTargetingRules.GetAllyField(source);
    }

    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        bool spellSource = source.Data != null && source.Data.cardType == CardType.SPELL;
        return EffectTargetingRules.GetAllyField(state, source.OwnerIndex, spellSource);
    }

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.HealTargets(targets, EffectValues.GetValue(effect, 0));
        onComplete?.Invoke();
    }

    public override void Simulate(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> targets,
        Random random)
    {
        if (targets == null)
        {
            return;
        }

        foreach (SimulatedTarget target in targets)
        {
            SimulationEffectActions.HealCard(state.FindCard(target.Id), EffectValues.GetValue(effect, 0));
        }
    }

    public override double ScoreSimulationTarget(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        SimulatedTarget target)
    {
        CardStateSnapshot card = state.FindCard(target.Id);
        if (card == null)
        {
            return double.MinValue;
        }

        return Math.Max(0, card.MaxHealth - card.Health) * 3 + EffectTargetingRules.GetSimulationAllyValue(card);
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (target is not CardController card || card.cardData == null)
        {
            return double.MinValue;
        }

        return Math.Max(0, card.maxHealth - card.health) * 3 + EffectTargetingRules.GetRuntimeAllyValue(card);
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0) * 1.5;
    }
}
