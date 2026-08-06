using System;
using System.Collections.Generic;

public sealed class BattleStateSimulator
{
    public List<SimulatedAction> GenerateLegalActions(BattleStateSnapshot state)
    {
        List<SimulatedAction> actions = new();
        if (state == null || state.IsGameOver || state.IsTurnEnded)
        {
            return actions;
        }

        PlayerStateSnapshot player = state.GetPlayer(state.CurrentPlayerIndex);
        if (player == null || player.Health <= 0)
        {
            return actions;
        }

        foreach (CardStateSnapshot card in player.Hand)
        {
            if (card == null || card.Data == null || card.Cost > player.Cost)
            {
                continue;
            }
            if (card.Data.cardType == CardType.Minion)
            {
                if (player.Field.Count < GameConst.fieldMax && HasRequiredConditions(state, card, TriggerType.Enter, false))
                {
                    AddTargetedActionVariants(state, card, SimulatedActionType.PlayHandCard, TriggerType.Enter, false, actions);
                }
            }
            else if (card.Data.cardType == CardType.SPELL
                && card.Data.effects != null
                && card.Data.effects.Count > 0
                && HasRequiredConditions(state, card, TriggerType.None, true))
            {
                AddTargetedActionVariants(state, card, SimulatedActionType.PlayHandCard, TriggerType.None, true, actions);
            }
        }

        foreach (CardStateSnapshot card in player.Field)
        {
            if (CanUseFieldCast(state, card))
            {
                AddTargetedActionVariants(state, card, SimulatedActionType.UseFieldCast, TriggerType.Cast, false, actions);
            }
        }

        foreach (CardStateSnapshot attacker in player.Field)
        {
            if (attacker == null || !attacker.CanAttack || attacker.AttacksRemaining <= 0 || attacker.Health <= 0 || attacker.IsDying)
            {
                continue;
            }
            foreach (PlayerStateSnapshot opponent in state.Players)
            {
                if (opponent.PlayerIndex == player.PlayerIndex || opponent.Health <= 0)
                {
                    continue;
                }
                bool hasGuard = HasGuard(opponent);
                foreach (CardStateSnapshot target in opponent.Field)
                {
                    if ((!hasGuard || target.HasPassive(PassiveType.Guard)) && !target.HasPassive(PassiveType.Stealth))
                    {
                        AddAction(actions, new SimulatedAction
                        {
                            Type = SimulatedActionType.AttackMinion,
                            SourceCardId = attacker.RuntimeId,
                            Targets = new List<SimulatedTarget> { SimulatedTarget.Card(target.RuntimeId) },
                        });
                    }
                }
                if (!hasGuard && attacker.CanAttackPlayer)
                {
                    AddAction(actions, new SimulatedAction
                    {
                        Type = SimulatedActionType.AttackPlayer,
                        SourceCardId = attacker.RuntimeId,
                        Targets = new List<SimulatedTarget> { SimulatedTarget.Player(opponent.PlayerIndex) },
                    });
                }
            }
        }

        AddAction(actions, new SimulatedAction { Type = SimulatedActionType.EndTurn });
        actions.Sort(CompareCanonicalActions);
        return actions;
    }

    public BattleStateSnapshot ApplyAction(BattleStateSnapshot sourceState, SimulatedAction action, Random random)
    {
        if (sourceState == null) throw new ArgumentNullException(nameof(sourceState));
        if (action == null) throw new ArgumentNullException(nameof(action));
        random ??= new Random();

        BattleStateSnapshot state = sourceState.Clone();
        PlayerStateSnapshot player = state.GetPlayer(state.CurrentPlayerIndex);
        if (player == null || state.IsGameOver || state.IsTurnEnded)
        {
            return state;
        }

        CardStateSnapshot source = action.SourceCardId != 0 ? state.FindCard(action.SourceCardId) : null;
        switch (action.Type)
        {
            case SimulatedActionType.PlayHandCard:
                PlayHandCard(state, player, source, action.Targets, random);
                break;
            case SimulatedActionType.UseFieldCast:
                if (source != null && source.OwnerIndex == player.PlayerIndex && source.State == CardState.Field && CanUseFieldCast(state, source))
                {
                    source.CastUsed = true;
                    EffectSimulationResolver.ResolveTrigger(state, source, TriggerType.Cast, false, action.Targets, random);
                }
                break;
            case SimulatedActionType.AttackMinion:
                if (source != null && action.Targets.Count > 0)
                {
                    CardStateSnapshot target = state.FindCard(action.Targets[0].Id);
                    if (CanAttackCard(state, source, target))
                    {
                        int sourceDamage = source.Attack;
                        int targetDamage = target.Attack;
                        List<CardStateSnapshot> neighbors = GetAdjacentMinions(state, target);
                        ConsumeAttack(source);
                        EffectSimulationResolver.DamageCard(state, source, target, sourceDamage, random);
                        EffectSimulationResolver.DamageCard(state, target, source, targetDamage, random);
                        if (source.HasPassive(PassiveType.Swingle))
                        {
                            foreach (CardStateSnapshot neighbor in neighbors)
                            {
                                EffectSimulationResolver.DamageCard(state, source, neighbor, sourceDamage, random);
                            }
                        }
                    }
                }
                break;
            case SimulatedActionType.AttackPlayer:
                if (source != null && action.Targets.Count > 0)
                {
                    PlayerStateSnapshot target = state.GetPlayer(action.Targets[0].Id);
                    if (CanAttackPlayer(state, source, target))
                    {
                        ConsumeAttack(source);
                        EffectSimulationResolver.DamagePlayer(state, source, target, source.Attack);
                    }
                }
                break;
            case SimulatedActionType.EndTurn:
                List<CardStateSnapshot> endingField = new(player.Field);
                foreach (CardStateSnapshot card in endingField)
                {
                    if (card.State == CardState.Field)
                    {
                        EffectSimulationResolver.ResolveTrigger(state, card, TriggerType.End, false, null, random);
                    }
                }
                StartNextTurn(state, random);
                break;
        }

        EffectSimulationResolver.UpdateGameOver(state);
        SynchronizeMaterializedHiddenCounts(state);
        return state;
    }

    private static void SynchronizeMaterializedHiddenCounts(BattleStateSnapshot state)
    {
        foreach (PlayerStateSnapshot snapshot in state.Players)
        {
            if (snapshot == null || !snapshot.HiddenInformationMaterialized)
            {
                continue;
            }

            snapshot.HiddenHandCount = snapshot.Hand.Count;
            snapshot.HiddenDeckCount = snapshot.DeckRemaining.Count;
        }
    }

    private static void StartNextTurn(BattleStateSnapshot state, Random random)
    {
        if (state.RootPlayerIndex < 0)
        {
            state.IsTurnEnded = true;
            return;
        }

        bool endingRoot = state.CurrentPlayerIndex == state.RootPlayerIndex;
        if (endingRoot)
        {
            state.RootEndTurnCount++;
            if (state.RootEndTurnCount >= state.MaxRootTurns)
            {
                state.IsTurnEnded = true;
                return;
            }
        }

        int nextIndex = (state.CurrentPlayerIndex + 1) % state.Players.Count;
        PlayerStateSnapshot nextPlayer = state.GetPlayer(nextIndex);
        if (nextPlayer == null)
        {
            state.IsTurnEnded = true;
            return;
        }

        foreach (CardStateSnapshot card in nextPlayer.Field)
        {
            if (card == null)
            {
                continue;
            }
            card.CanAttack = true;
            card.CanAttackPlayer = true;
            card.AttacksRemaining = card.HasPassive(PassiveType.Windfury) ? 2 : 1;
        }

        nextPlayer.MaxCost = Math.Min(GameConst.costMax, nextPlayer.MaxCost + 1);
        nextPlayer.Cost = nextPlayer.MaxCost;

        List<CardStateSnapshot> startingField = new(nextPlayer.Field);
        foreach (CardStateSnapshot card in startingField)
        {
            if (card.State == CardState.Field)
            {
                EffectSimulationResolver.ResolveTrigger(state, card, TriggerType.Start, false, null, random);
            }
        }

        if (nextPlayer.DeckRemaining.Count > 0)
        {
            CardStateSnapshot drawn = nextPlayer.DeckRemaining[0];
            nextPlayer.DeckRemaining.RemoveAt(0);
            if (drawn != null)
            {
                drawn.State = CardState.Hand;
                if (nextPlayer.Hand.Count >= GameConst.handMax)
                {
                    drawn.ResetRuntimeState(CardState.Graveyard);
                    nextPlayer.Graveyard.Add(drawn);
                }
                else
                {
                    nextPlayer.Hand.Add(drawn);
                }
            }
        }

        state.CurrentPlayerIndex = nextIndex;
        state.IsTurnEnded = false;
    }

    public static bool IsTargetedEffect(EffectType effectType)
    {
        return EffectRegistry.Get(effectType).IsTargeted;
    }

    public static bool CheckConditions(BattleStateSnapshot state, CardStateSnapshot source, List<ConditionType> conditions)
    {
        if (conditions == null || conditions.Count == 0) return true;
        foreach (ConditionType condition in conditions)
        {
            if (condition != ConditionType.None && !CheckCondition(state, source, condition)) return false;
        }
        return true;
    }

    public static bool HasRequiredCondition(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        if (effect == null) return false;
        if (!HasConditions(effect.conditionTypes)) return true;
        if (HasEffects(effect.elseEffects)) return true;
        return CheckConditions(state, source, effect.conditionTypes);
    }

    public static int GetSelectionCount(CardEffectData effect)
    {
        return EffectRegistry.Get(effect != null ? effect.effectType : EffectType.None).GetSelectionCount(effect);
    }

    private static void PlayHandCard(
        BattleStateSnapshot state,
        PlayerStateSnapshot player,
        CardStateSnapshot card,
        List<SimulatedTarget> targets,
        Random random)
    {
        if (card == null || card.Data == null || card.OwnerIndex != player.PlayerIndex || card.State != CardState.Hand || card.Cost > player.Cost)
        {
            return;
        }
        if (card.Data.cardType == CardType.Minion)
        {
            if (player.Field.Count >= GameConst.fieldMax || !HasRequiredConditions(state, card, TriggerType.Enter, false)) return;
            player.Cost -= card.Cost;
            player.Hand.Remove(card);
            card.State = CardState.Field;
            card.CastUsed = false;
            card.CanAttack = card.HasAnyPassive(PassiveType.Rush, PassiveType.Charge);
            card.CanAttackPlayer = card.HasPassive(PassiveType.Charge);
            card.AttacksRemaining = card.HasPassive(PassiveType.Windfury) ? 2 : 1;
            card.IsStealth = card.HasPassive(PassiveType.Stealth);
            card.HolyShield = card.HasPassive(PassiveType.HolyShield) ? 1 : 0;
            player.Field.Add(card);
            EffectSimulationResolver.ResolveTrigger(state, card, TriggerType.Enter, false, targets, random);
            return;
        }
        if (card.Data.cardType != CardType.SPELL || !HasRequiredConditions(state, card, TriggerType.None, true)) return;
        player.Cost -= card.Cost;
        player.Hand.Remove(card);
        EffectSimulationResolver.ResolveTrigger(state, card, TriggerType.None, true, targets, random);
        card.ResetRuntimeState(CardState.Graveyard);
        player.Graveyard.Add(card);
    }

    private static bool HasRequiredConditions(BattleStateSnapshot state, CardStateSnapshot card, TriggerType triggerType, bool allEffects)
    {
        if (card == null || card.Data == null || card.Data.effects == null) return !allEffects;
        bool matched = false;
        foreach (CardEffectData effect in card.Data.effects)
        {
            if (effect == null || (!allEffects && effect.triggerType != triggerType)) continue;
            matched = true;
            if (!HasRequiredCondition(state, card, effect)) return false;
        }
        return !allEffects || matched;
    }

    private static bool CanUseFieldCast(BattleStateSnapshot state, CardStateSnapshot card)
    {
        if (card == null || card.Data == null || card.State != CardState.Field || card.OwnerIndex != state.CurrentPlayerIndex
            || card.IsSilence || card.CastUsed || card.Data.cardType != CardType.Minion || card.Data.effects == null)
        {
            return false;
        }
        bool found = false;
        foreach (CardEffectData effect in card.Data.effects)
        {
            if (effect == null || effect.triggerType != TriggerType.Cast) continue;
            found = true;
            if (!HasRequiredCondition(state, card, effect)) return false;
        }
        return found;
    }

    private static bool CanAttackCard(BattleStateSnapshot state, CardStateSnapshot attacker, CardStateSnapshot target)
    {
        if (!CanAttack(state, attacker) || target == null || target.State != CardState.Field || target.OwnerIndex == attacker.OwnerIndex) return false;
        if (target.HasPassive(PassiveType.Stealth)) return false;
        PlayerStateSnapshot owner = state.GetPlayer(target.OwnerIndex);
        return owner != null && (!HasGuard(owner) || target.HasPassive(PassiveType.Guard));
    }

    private static bool CanAttackPlayer(BattleStateSnapshot state, CardStateSnapshot attacker, PlayerStateSnapshot target)
    {
        return CanAttack(state, attacker) && attacker.CanAttackPlayer && target != null && target.PlayerIndex != attacker.OwnerIndex && !HasGuard(target);
    }

    private static bool CanAttack(BattleStateSnapshot state, CardStateSnapshot attacker)
    {
        return attacker != null && attacker.State == CardState.Field && attacker.CanAttack && attacker.AttacksRemaining > 0 && !attacker.IsDying
            && attacker.OwnerIndex == state.CurrentPlayerIndex;
    }

    private static void ConsumeAttack(CardStateSnapshot attacker)
    {
        attacker.AttacksRemaining = Math.Max(0, attacker.AttacksRemaining - 1);
        if (attacker.AttacksRemaining <= 0)
        {
            attacker.CanAttack = false;
        }

        attacker.IsStealth = false;
    }

    private static List<CardStateSnapshot> GetAdjacentMinions(BattleStateSnapshot state, CardStateSnapshot target)
    {
        List<CardStateSnapshot> neighbors = new();
        if (target == null)
        {
            return neighbors;
        }

        PlayerStateSnapshot owner = state.GetPlayer(target.OwnerIndex);
        if (owner == null)
        {
            return neighbors;
        }

        int index = owner.Field.IndexOf(target);
        if (index > 0)
        {
            neighbors.Add(owner.Field[index - 1]);
        }

        if (index >= 0 && index < owner.Field.Count - 1)
        {
            neighbors.Add(owner.Field[index + 1]);
        }

        return neighbors;
    }

    private static bool CheckCondition(BattleStateSnapshot state, CardStateSnapshot source, ConditionType condition)
    {
        if (state == null || source == null) return false;
        PlayerStateSnapshot owner = state.GetPlayer(source.OwnerIndex);
        if (owner == null) return false;
        switch (condition)
        {
            case ConditionType.None: return true;
            case ConditionType.ThreeMoreHand: return owner.Hand.Count >= 3;
            case ConditionType.HasEnemy:
                foreach (PlayerStateSnapshot player in state.Players) if (player.PlayerIndex != owner.PlayerIndex && player.Field.Count > 0) return true;
                return false;
            case ConditionType.HasOther:
                foreach (CardStateSnapshot card in owner.Field) if (card.RuntimeId != source.RuntimeId) return true;
                return false;
            case ConditionType.HasAlly: return owner.Field.Count > 0;
            case ConditionType.HasDiedAlly: return owner.Graveyard.Count > 0;
            case ConditionType.HasDiedEnemy:
                foreach (PlayerStateSnapshot player in state.Players) if (player.PlayerIndex != owner.PlayerIndex && player.Graveyard.Count > 0) return true;
                return false;
            case ConditionType.HasEmptyField: return owner.Field.Count < GameConst.fieldMax;
            case ConditionType.HasNonMagicalImmunityAlly:
                foreach (CardStateSnapshot card in owner.Field) if (card != null && !card.HasPassive(PassiveType.MagicImmunity)) return true;
                return false;
            case ConditionType.HasNonMagicalImmunityEnemy:
                foreach (PlayerStateSnapshot player in state.Players)
                {
                    if (player.PlayerIndex == owner.PlayerIndex) continue;
                    foreach (CardStateSnapshot card in player.Field) if (card != null && !card.HasPassive(PassiveType.MagicImmunity)) return true;
                }
                return false;
            case ConditionType.HasNonMagicalImmunityOther:
                foreach (PlayerStateSnapshot player in state.Players)
                {
                    foreach (CardStateSnapshot card in player.Field) if (card != null && card.RuntimeId != source.RuntimeId && !card.HasPassive(PassiveType.MagicImmunity)) return true;
                }
                return false;
            default: return false;
        }
    }

    private static void AddTargetedActionVariants(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        SimulatedActionType actionType,
        TriggerType trigger,
        bool allEffects,
        List<SimulatedAction> actions)
    {
        CardEffectData targetedEffect = FindFirstTargetedEffect(state, source, trigger, allEffects);
        if (targetedEffect == null)
        {
            AddAction(actions, new SimulatedAction { Type = actionType, SourceCardId = source.RuntimeId });
            return;
        }

        List<SimulatedTarget> candidates = AITargetSelector.GetCandidates(state, source, targetedEffect);
        int count = EffectRegistry.Get(targetedEffect.effectType).GetSimulationSelectionCount(state, source, targetedEffect);
        if (count <= 0 || candidates.Count == 0)
        {
            AddAction(actions, new SimulatedAction { Type = actionType, SourceCardId = source.RuntimeId });
            return;
        }
        candidates.Sort(CompareTargets);
        int requiredCount = Math.Min(Math.Max(0, count), candidates.Count);
        AddTargetCombinations(candidates, requiredCount, 0, new List<SimulatedTarget>(), targets =>
        {
            AddAction(actions, new SimulatedAction
            {
                Type = actionType,
                SourceCardId = source.RuntimeId,
                Targets = targets,
            });
        });
    }

    private static CardEffectData FindFirstTargetedEffect(BattleStateSnapshot state, CardStateSnapshot source, TriggerType trigger, bool allEffects)
    {
        if (source.Data.effects == null) return null;
        foreach (CardEffectData effect in source.Data.effects)
        {
            if (effect == null || (!allEffects && effect.triggerType != trigger)) continue;
            CardEffectData resolved = ResolveConditionalBranch(state, source, effect);
            if (resolved != null) return resolved;
        }
        return null;
    }

    private static CardEffectData ResolveConditionalBranch(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        bool passed = CheckConditions(state, source, effect.conditionTypes);
        bool hasConditions = HasConditions(effect.conditionTypes);
        bool hasBranches = HasEffects(effect.thenEffects) || HasEffects(effect.elseEffects);
        if (hasConditions && hasBranches)
        {
            List<CardEffectData> branch = passed ? effect.thenEffects : effect.elseEffects;
            if (branch != null)
            {
                foreach (CardEffectData nested in branch)
                {
                    CardEffectData result = ResolveConditionalBranch(state, source, nested);
                    if (result != null) return result;
                }
            }
            return null;
        }
        return (!hasConditions || passed) && IsTargetedEffect(effect.effectType) ? effect : null;
    }

    private static bool HasGuard(PlayerStateSnapshot player)
    {
        foreach (CardStateSnapshot card in player.Field) if (card.HasPassive(PassiveType.Guard) && !card.IsStealth) return true;
        return false;
    }

    private static bool HasConditions(List<ConditionType> conditions)
    {
        if (conditions == null) return false;
        foreach (ConditionType condition in conditions) if (condition != ConditionType.None) return true;
        return false;
    }

    private static bool HasEffects(List<CardEffectData> effects) => effects != null && effects.Count > 0;

    private static void AddTargetCombinations(
        List<SimulatedTarget> candidates,
        int requiredCount,
        int startIndex,
        List<SimulatedTarget> selected,
        Action<List<SimulatedTarget>> add)
    {
        if (selected.Count == requiredCount)
        {
            add(new List<SimulatedTarget>(selected));
            return;
        }

        int remaining = requiredCount - selected.Count;
        for (int index = startIndex; index <= candidates.Count - remaining; index++)
        {
            selected.Add(candidates[index]);
            AddTargetCombinations(candidates, requiredCount, index + 1, selected, add);
            selected.RemoveAt(selected.Count - 1);
        }
    }

    private static void AddAction(List<SimulatedAction> actions, SimulatedAction action)
    {
        actions.Add(action);
    }

    private static int CompareCanonicalActions(SimulatedAction left, SimulatedAction right)
    {
        int type = left.Type.CompareTo(right.Type);
        if (type != 0) return type;
        int source = left.SourceCardId.CompareTo(right.SourceCardId);
        if (source != 0) return source;
        int targetCount = left.Targets.Count.CompareTo(right.Targets.Count);
        if (targetCount != 0) return targetCount;
        for (int index = 0; index < left.Targets.Count; index++)
        {
            int target = CompareTargets(left.Targets[index], right.Targets[index]);
            if (target != 0) return target;
        }
        return 0;
    }

    private static int CompareTargets(SimulatedTarget left, SimulatedTarget right)
    {
        int kind = left.Kind.CompareTo(right.Kind);
        return kind != 0 ? kind : left.Id.CompareTo(right.Id);
    }
}
