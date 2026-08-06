using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

[CardEffect(EffectType.ReviveAlly, "复活友方")]
public sealed class ReviveAllyEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.ReviveAlly;
    public override string Label => "复活友方";
    public override bool IsTargeted => true;
    public override TargetSelectionZone SelectionZone => TargetSelectionZone.Graveyard;

    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(1, "复活数量", 1, false),
    };

    public override int GetRuntimeSelectionCount(CardController source, CardEffectData effect)
    {
        if (source == null || source.player == null || source.player.fieldController == null)
        {
            return 0;
        }

        int openSlots = GameConst.fieldMax - source.player.fieldController.fieldCards.Count;
        if (openSlots <= 0)
        {
            return 0;
        }

        return Math.Min(GetSelectionCount(effect), openSlots);
    }

    public override int GetSimulationSelectionCount(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        PlayerStateSnapshot owner = state != null ? state.GetPlayer(source.OwnerIndex) : null;
        if (owner == null)
        {
            return 0;
        }

        return Math.Min(GetSelectionCount(effect), Math.Max(0, GameConst.fieldMax - owner.Field.Count));
    }

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        return EffectTargetingRules.GetAllyGraveyardMinions(source.player);
    }

    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        return EffectTargetingRules.GetAllyGraveyardMinions(state, source.OwnerIndex);
    }

    public override void ApplyRuntime(
        CardEffectContext context,
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> targets,
        Action onComplete)
    {
        RuntimeEffectActions.ReviveAllies(source.player, targets);
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

        PlayerStateSnapshot owner = state.GetPlayer(source.OwnerIndex);
        foreach (SimulatedTarget target in targets)
        {
            SimulationEffectActions.Revive(owner, state.FindCard(target.Id));
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

        return EffectTargetingRules.GetSimulationAllyValue(card) + EffectTargetingRules.CountUsefulEffects(card) * 2;
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (target is not CardController card || card.cardData == null)
        {
            return double.MinValue;
        }

        return EffectTargetingRules.GetRuntimeAllyValue(card) + EffectTargetingRules.CountUsefulEffects(card.cardData) * 2;
    }

    public override double HeuristicScore(CardEffectData effect)
    {
        return 14;
    }
}
