using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.DealDamageToEnemy, "选择敌方造成伤害")]
public sealed class DealDamageToEnemyEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.DealDamageToEnemy;
    public override string Label => "选择敌方造成伤害";
    public override bool IsTargeted => true;

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "伤害值", 0, true),
        new EffectValueParameter(1, "目标数", 1, false),
    };

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        return EffectTargetingRules.GetEnemyCharacters(source);
    }

    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        bool spellSource = source.Data != null && source.Data.cardType == CardType.SPELL;
        return EffectTargetingRules.GetEnemyCharacters(state, source.OwnerIndex, spellSource);
    }

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.DamageTargets(source, targets, EffectValues.GetValue(effect, 0));
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
        if (targets == null)
        {
            return;
        }

        foreach (SimulatedTarget target in targets)
        {
            if (target.Kind == SimulatedTargetKind.Player)
            {
                EffectSimulationResolver.DamagePlayer(state, source, state.GetPlayer(target.Id), damage);
            }
            else
            {
                EffectSimulationResolver.DamageCard(state, source, state.FindCard(target.Id), damage, random);
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

        int damage = EffectValues.GetValue(effect, 0);
        double score = EffectTargetingRules.GetSimulationThreat(card);
        if (damage == card.Health)
        {
            score += 3;
        }
        else if (damage > card.Health + 3)
        {
            score -= damage - card.Health - 3;
        }
        else if (damage < card.Health)
        {
            score -= (card.Health - damage) * 0.5;
        }

        return score;
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (target is not CardController card || card.cardData == null)
        {
            return double.MinValue;
        }

        int damage = EffectValues.GetValue(effect, 0);
        double score = EffectTargetingRules.GetRuntimeThreat(card);
        if (damage == card.health)
        {
            score += 3;
        }
        else if (damage > card.health + 3)
        {
            score -= damage - card.health - 3;
        }
        else if (damage < card.health)
        {
            score -= (card.health - damage) * 0.5;
        }

        return score;
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0) * 4;
    }
}
