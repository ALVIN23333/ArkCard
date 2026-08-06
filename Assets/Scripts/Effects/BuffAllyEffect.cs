using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.BuffAlly, "强化友方")]
public sealed class BuffAllyEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.BuffAlly;
    public override string Label => "强化友方";
    public override bool IsTargeted => true;
    public override int SelectionCountIndex => 2;

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "攻击变化", 0, true),
        new EffectValueParameter(1, "生命变化", 0, true),
        new EffectValueParameter(2, "目标数", 1, false),
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
        RuntimeEffectActions.BuffTargets(targets, EffectValues.GetValue(effect, 0), EffectValues.GetValue(effect, 1));
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
            SimulationEffectActions.AddStats(
                state.FindCard(target.Id),
                EffectValues.GetValue(effect, 0),
                EffectValues.GetValue(effect, 1));
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

        return EffectTargetingRules.GetSimulationAllyValue(card) + (card.CanAttack ? 5 : 0);
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (target is not CardController card || card.cardData == null)
        {
            return double.MinValue;
        }

        return EffectTargetingRules.GetRuntimeAllyValue(card) + (card.canAttack ? 5 : 0);
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return EffectValues.GetValue(effect, 0) * 2 + EffectValues.GetValue(effect, 1);
    }
}
