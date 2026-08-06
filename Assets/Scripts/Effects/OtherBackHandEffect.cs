using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.OtherBackHand, "其他随从回手")]
public sealed class OtherBackHandEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.OtherBackHand;
    public override string Label => "其他随从回手";
    public override bool IsTargeted => true;

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(1, "目标数", 1, false),
    };

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        return EffectTargetingRules.GetOtherField(source);
    }

    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        return EffectTargetingRules.GetOtherField(state, source);
    }

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.ReturnTargetsToOwnerHand(targets);
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
            SimulationEffectActions.ReturnToHand(state, state.FindCard(target.Id));
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
            + card.Cost
            + EffectTargetingRules.GetSimulationBuffAmount(card) * 1.5;
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (target is not CardController card || card.cardData == null)
        {
            return double.MinValue;
        }

        return EffectTargetingRules.GetRuntimeThreat(card)
            + card.cost
            + EffectTargetingRules.GetRuntimeBuffAmount(card) * 1.5;
    }
}
