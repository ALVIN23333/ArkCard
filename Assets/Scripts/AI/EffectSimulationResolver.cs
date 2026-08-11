using System;
using System.Collections.Generic;

public static class EffectSimulationResolver
{
    public static void ResolveTrigger(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        TriggerType trigger,
        bool executeAllEffects,
        List<SimulatedTarget> explicitTargets,
        Random random)
    {
        if (state == null || source == null || source.Data == null || source.IsSilence || source.Data.effects == null) return;
        foreach (CardEffectData effect in source.Data.effects)
        {
            if (effect != null && (executeAllEffects || effect.triggerType == trigger))
            {
                ResolveEffect(state, source, effect, explicitTargets, random);
            }
        }
    }

    public static void DamageCard(BattleStateSnapshot state, CardStateSnapshot source, CardStateSnapshot target, int damage, Random random)
    {
        if (target == null || damage <= 0 || target.State == CardState.Graveyard || target.IsDying) return;

        int damageDealt = 0;
        if (target.HolyShield > 0)
        {
            target.HolyShield--;
        }
        else
        {
            int before = target.Health;
            target.Health = Math.Max(0, target.Health - damage);
            damageDealt = before - target.Health;
        }

        if (source != null && damageDealt > 0 && source.HasPassive(PassiveType.Lifesteal))
        {
            PlayerStateSnapshot owner = state.GetPlayer(source.OwnerIndex);
            if (owner != null)
            {
                owner.Health = Math.Min(owner.MaxHealth, owner.Health + damageDealt);
            }
        }

        if (damageDealt > 0)
        {
            ResolveTrigger(state, target, TriggerType.Hurt, false, null, random);
        }

        if (target.Health <= 0 && !target.IsDying)
        {
            KillCard(state, target, random);
            return;
        }

        if (source != null
            && damageDealt > 0
            && source.HasPassive(PassiveType.Poisonous)
            && target.Data != null
            && target.Data.cardType == CardType.Minion
            && target.State == CardState.Field
            && !target.IsDying)
        {
            KillCard(state, target, random);
            return;
        }

        UpdateGameOver(state);
    }

    public static void DamagePlayer(BattleStateSnapshot state, CardStateSnapshot source, PlayerStateSnapshot target, int damage)
    {
        if (target == null || damage <= 0) return;
        target.Health -= damage;
        if (source != null && source.HasPassive(PassiveType.Lifesteal))
        {
            PlayerStateSnapshot owner = state.GetPlayer(source.OwnerIndex);
            if (owner != null)
            {
                owner.Health = Math.Min(owner.MaxHealth, owner.Health + damage);
            }
        }
        UpdateGameOver(state);
    }

    public static void UpdateGameOver(BattleStateSnapshot state)
    {
        if (state == null) return;
        int living = 0;
        foreach (PlayerStateSnapshot player in state.Players) if (player.Health > 0) living++;
        state.IsGameOver = living <= 1;
    }

    private static void ResolveEffect(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> explicitTargets,
        Random random)
    {
        bool passed = BattleStateSimulator.CheckConditions(state, source, effect.conditionTypes);
        bool hasConditions = HasConditions(effect.conditionTypes);
        bool hasBranches = HasEffects(effect.thenEffects) || HasEffects(effect.elseEffects);
        if (hasConditions && hasBranches)
        {
            ResolveEffectList(state, source, passed ? effect.thenEffects : effect.elseEffects, explicitTargets, random);
            return;
        }
        if (hasConditions && !passed) return;

        ICardEffectDefinition definition = EffectRegistry.Get(effect.effectType);
        List<SimulatedTarget> resolvedTargets = definition.RequiresTargetSelection(effect)
            ? ResolveTargets(state, source, effect, explicitTargets)
            : null;
        definition.Simulate(state, source, effect, resolvedTargets, random);
        UpdateGameOver(state);
    }

    private static void ResolveEffectList(BattleStateSnapshot state, CardStateSnapshot source, List<CardEffectData> effects, List<SimulatedTarget> targets, Random random)
    {
        if (effects == null) return;
        foreach (CardEffectData effect in effects) if (effect != null) ResolveEffect(state, source, effect, targets, random);
    }

    private static List<SimulatedTarget> ResolveTargets(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> explicitTargets)
    {
        ICardEffectDefinition definition = EffectRegistry.Get(effect.effectType);
        List<SimulatedTarget> candidates = definition.GetSimulationCandidates(state, source, effect);
        int count = definition.GetSimulationSelectionCount(state, source, effect);
        List<SimulatedTarget> resolved = new();
        if (explicitTargets != null)
        {
            foreach (SimulatedTarget target in explicitTargets)
            {
                if (resolved.Count >= count) break;
                if (Contains(candidates, target) && !Contains(resolved, target)) resolved.Add(target);
            }
        }
        if (resolved.Count < count)
        {
            List<SimulatedTarget> remaining = new(candidates);
            remaining.RemoveAll(candidate => Contains(resolved, candidate));
            resolved.AddRange(AITargetSelector.SelectTargets(state, source, effect, count - resolved.Count, remaining));
        }
        return resolved;
    }

    internal static void DamageFields(BattleStateSnapshot state, CardStateSnapshot source, int damage, bool enemiesOnly, Random random)
    {
        List<CardStateSnapshot> targets = new();
        foreach (PlayerStateSnapshot player in state.Players)
        {
            foreach (CardStateSnapshot card in player.Field)
            {
                if ((!enemiesOnly && card.RuntimeId != source.RuntimeId) || (enemiesOnly && player.PlayerIndex != source.OwnerIndex)) targets.Add(card);
            }
        }
        foreach (CardStateSnapshot target in targets) DamageCard(state, source, target, damage, random);
    }

    internal static void KillCard(BattleStateSnapshot state, CardStateSnapshot card, Random random)
    {
        if (card == null || card.State == CardState.Graveyard || card.IsDying) return;
        card.Health = 0;
        card.IsDying = true;
        if (!card.IsSilence) ResolveTrigger(state, card, TriggerType.Died, false, null, random);
        PlayerStateSnapshot owner = state.GetPlayer(card.OwnerIndex);
        owner.Hand.Remove(card);
        owner.Field.Remove(card);
        owner.DeckRemaining.Remove(card);
        if (!owner.Graveyard.Contains(card)) owner.Graveyard.Add(card);
        card.ResetRuntimeState(CardState.Graveyard);
    }

    private static bool HasEffects(List<CardEffectData> effects) => effects != null && effects.Count > 0;

    private static bool HasConditions(List<ConditionType> conditions)
    {
        if (conditions == null) return false;
        foreach (ConditionType condition in conditions) if (condition != ConditionType.None) return true;
        return false;
    }

    private static bool Contains(List<SimulatedTarget> targets, SimulatedTarget target)
    {
        foreach (SimulatedTarget candidate in targets) if (candidate.Equals(target)) return true;
        return false;
    }
}
