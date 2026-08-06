using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.SlienceEnemy, "沉默敌方")]
public sealed class SilenceEnemyEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.SlienceEnemy;
    public override string Label => "沉默敌方";
    public override bool IsTargeted => true;

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(1, "目标数", 1, false),
    };

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        return EffectTargetingRules.GetEnemyField(source);
    }

    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        bool spellSource = source.Data != null && source.Data.cardType == CardType.SPELL;
        return EffectTargetingRules.GetEnemyField(state, source.OwnerIndex, spellSource);
    }

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.SilenceTargets(targets);
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
            CardStateSnapshot card = state.FindCard(target.Id);
            if (card != null)
            {
                card.IsSilence = true;
            }
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

        return EffectTargetingRules.GetSimulationThreat(card)
            + EffectTargetingRules.GetSimulationPassiveBonus(card) * 2
            + EffectTargetingRules.GetSimulationBuffAmount(card);
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (target is not CardController card || card.cardData == null)
        {
            return double.MinValue;
        }

        return EffectTargetingRules.GetRuntimeThreat(card)
            + EffectTargetingRules.GetRuntimePassiveBonus(card) * 2
            + EffectTargetingRules.GetRuntimeBuffAmount(card);
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return 9;
    }
}
