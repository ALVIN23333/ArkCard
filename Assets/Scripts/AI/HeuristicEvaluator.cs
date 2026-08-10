using System;
using System.Collections.Generic;

public static class HeuristicEvaluator
{
    public static double Evaluate(BattleStateSnapshot state, int rootPlayerIndex)
    {
        if (state == null) return 0;
        PlayerStateSnapshot root = state.GetPlayer(rootPlayerIndex);
        if (root == null) return 0;

        if (root.Health <= 0) return -1;
        bool hasLivingOpponent = false;
        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player.PlayerIndex != rootPlayerIndex && player.Health > 0)
            {
                hasLivingOpponent = true;
                break;
            }
        }
        if (!hasLivingOpponent) return 1;

        double handValue = 1.5 * Math.Min(root.Hand.Count, GameConst.handMax);
        if (root.Hand.Count >= GameConst.handMax)
        {
            handValue -= 4;
        }
        double raw = 3 * root.Health + handValue - 0.8 * root.Cost;
        raw += ScoreFriendlyBoard(root.Field);

        foreach (PlayerStateSnapshot opponent in state.Players)
        {
            if (opponent.PlayerIndex == rootPlayerIndex) continue;
            raw -= 4 * opponent.Health + opponent.Hand.Count;
            raw -= ScoreEnemyBoard(opponent.Field);
        }

        bool hasLethal = HasImmediateLethal(state, rootPlayerIndex);
        if (!hasLethal)
        {
            foreach (PlayerStateSnapshot opponent in state.Players)
            {
                if (opponent.PlayerIndex == rootPlayerIndex) continue;
                foreach (CardStateSnapshot card in opponent.Field)
                {
                    if (card != null && card.HasPassive(PassiveType.Lifesteal))
                    {
                        raw -= 2 * Math.Max(0, card.Attack);
                    }
                }
            }
        }
        if (IsExposedAfterAllInAttack(state, rootPlayerIndex))
        {
            raw -= 4;
        }
        raw += GetTempoScore(root);
        if (hasLethal) raw += 100;
        if (IsAtHighRisk(state, rootPlayerIndex)) raw -= 40;
        return Math.Tanh(raw / 100.0);
    }

    public static bool HasImmediateLethal(BattleStateSnapshot state, int playerIndex)
    {
        PlayerStateSnapshot player = state.GetPlayer(playerIndex);
        if (player == null) return false;
        int attack = 0;
        foreach (CardStateSnapshot card in player.Field)
        {
            if (card.CanAttack && card.Health > 0) attack += Math.Max(0, card.Attack);
        }
        foreach (PlayerStateSnapshot opponent in state.Players)
        {
            if (opponent.PlayerIndex == playerIndex || HasGuard(opponent)) continue;
            if (attack >= opponent.Health) return true;
        }
        return false;
    }

    public static double ScoreAction(BattleStateSnapshot state, SimulatedAction action)
    {
        if (state == null || action == null) return double.MinValue;
        CardStateSnapshot source = action.SourceCardId != 0 ? state.FindCard(action.SourceCardId) : null;
        switch (action.Type)
        {
            case SimulatedActionType.PlayHandCard:
                if (source == null || source.Data == null) return double.MinValue;
                double playScore = source.Data.aiBasePriority - source.Cost * 0.25;
                if (source.Data.cardType == CardType.Minion)
                {
                    playScore += 2 * source.Attack + source.Health + PassiveActionBonus(source);
                }
                else
                {
                    playScore += ScoreEffects(source.Data.effects);
                }
                playScore += RoleBonus(source.Data.aiRole);
                if (source.Data.aiPlayStyle == AIPlayStyle.ComboReserve && state.GetPlayer(source.OwnerIndex).Cost < source.Data.aiComboReserveThreshold)
                {
                    playScore -= 25;
                }
                PlayerStateSnapshot cardOwner = state.GetPlayer(source.OwnerIndex);
                if (cardOwner != null && HasDrawEffect(source.Data.effects) && cardOwner.Hand.Count >= GameConst.handMax - 1)
                {
                    playScore -= (cardOwner.Hand.Count - (GameConst.handMax - 2)) * 8;
                }
                return playScore + ScoreTargets(state, source, action);
            case SimulatedActionType.UseFieldCast:
                return source != null && source.Data != null
                    ? source.Data.aiBasePriority + ScoreEffects(source.Data.effects) + ScoreTargets(state, source, action)
                    : double.MinValue;
            case SimulatedActionType.AttackPlayer:
                if (source == null || action.Targets.Count == 0) return double.MinValue;
                PlayerStateSnapshot player = state.GetPlayer(action.Targets[0].Id);
                if (player == null) return double.MinValue;
                int lethalBonus = source.Data != null ? source.Data.aiLethalBonus : 0;
                bool lethal = source.Attack >= player.Health;
                // Poisonous minions waste their effect on the hero: only attack the hero
                // when the hero is about to die or there is no attackable enemy minion.
                if (source.HasPassive(PassiveType.Poisonous) && !lethal && HasAttackableEnemyMinion(state, source))
                {
                    return 0;
                }
                double faceScore = lethal ? 1000 + lethalBonus : source.Attack * 5 + lethalBonus * 2;
                if (source.Data != null)
                {
                    if (source.Data.aiPlayStyle == AIPlayStyle.Aggressive) faceScore *= 1.5;
                    else if (source.Data.aiPlayStyle == AIPlayStyle.Defensive) faceScore *= 0.5;
                }
                return faceScore;
            case SimulatedActionType.AttackMinion:
                if (source == null || action.Targets.Count == 0) return double.MinValue;
                CardStateSnapshot target = state.FindCard(action.Targets[0].Id);
                if (target == null) return double.MinValue;
                double trade = 2 * target.Attack + target.Health;
                if (source.Attack >= target.Health) trade += 12;
                if (target.Attack >= source.Health) trade -= 2 * source.Attack + source.Health;
                if (target.HasPassive(PassiveType.Lifesteal)) trade += 8 + 2 * Math.Max(0, target.Attack);
                if (source.Data != null)
                {
                    if (source.Data.aiPlayStyle == AIPlayStyle.Aggressive) trade *= 0.8;
                    else if (source.Data.aiPlayStyle == AIPlayStyle.Defensive) trade *= 1.2;
                }
                return trade;
            case SimulatedActionType.EndTurn:
                PlayerStateSnapshot current = state.GetPlayer(state.CurrentPlayerIndex);
                return -25 - (current != null ? current.Cost * 0.8 : 0);
            default:
                return 0;
        }
    }

    private static double ScoreFriendlyBoard(List<CardStateSnapshot> field)
    {
        double score = 0;
        foreach (CardStateSnapshot card in field)
        {
            score += 2 * card.Attack + card.Health;
            if (card.CanAttack) score += 1;
            if (card.HasPassive(PassiveType.Guard)) score += 5;
            score += ThreatPassiveBonus(card);
        }
        return score;
    }

    private static double ScoreEnemyBoard(List<CardStateSnapshot> field)
    {
        double score = 0;
        foreach (CardStateSnapshot card in field)
        {
            score += 2.5 * card.Attack + 1.2 * card.Health;
            if (card.CanAttack) score += 1;
            if (card.HasPassive(PassiveType.Guard)) score += 6;
            score += ThreatPassiveBonus(card);
        }
        return score;
    }

    private static double GetTempoScore(PlayerStateSnapshot player)
    {
        double score = 0;
        int handLimit = Math.Min(player.Hand.Count, GameConst.handMax);
        for (int i = 0; i < handLimit; i++)
        {
            CardStateSnapshot card = player.Hand[i];
            if (card.Cost <= player.Cost)
            {
                score += 0.5 + Math.Max(0, card.Attack + card.Health) * 0.1;
            }
        }
        foreach (CardStateSnapshot card in player.Field)
        {
            if (card.CanAttack) score += 0.75;
            if (!card.CastUsed && HasCastEffect(card)) score += 0.5;
        }
        return score;
    }

    private static bool IsAtHighRisk(BattleStateSnapshot state, int playerIndex)
    {
        PlayerStateSnapshot player = state.GetPlayer(playerIndex);
        if (player == null) return false;
        int incomingAttack = 0;
        foreach (PlayerStateSnapshot opponent in state.Players)
        {
            if (opponent.PlayerIndex == playerIndex) continue;
            foreach (CardStateSnapshot card in opponent.Field)
            {
                incomingAttack += Math.Max(0, card.Attack);
            }
        }
        return incomingAttack >= player.Health && !HasGuard(player);
    }

    private static bool HasGuard(PlayerStateSnapshot player)
    {
        foreach (CardStateSnapshot card in player.Field)
        {
            if (card.HasPassive(PassiveType.Guard) && !card.IsStealth) return true;
        }
        return false;
    }

    private static bool HasCastEffect(CardStateSnapshot card)
    {
        if (card.Data == null || card.Data.effects == null || card.IsSilence) return false;
        foreach (CardEffectData effect in card.Data.effects)
        {
            if (effect != null && effect.triggerType == TriggerType.Cast) return true;
        }
        return false;
    }

    private static double ScoreTargets(BattleStateSnapshot state, CardStateSnapshot source, SimulatedAction action)
    {
        if (action.Targets.Count == 0 || source.Data.effects == null) return 0;
        CardEffectData targetedEffect = FindFirstTargetedEffect(source.Data.effects);
        if (targetedEffect == null) return 0;
        double score = 0;
        foreach (SimulatedTarget target in action.Targets)
        {
            score += AITargetSelector.ScoreTarget(state, source, targetedEffect, target) * 0.25;
        }
        return score;
    }

    private static CardEffectData FindFirstTargetedEffect(List<CardEffectData> effects)
    {
        foreach (CardEffectData effect in effects)
        {
            if (effect != null && BattleStateSimulator.IsTargetedEffect(effect)) return effect;
        }
        return null;
    }

    private static double ScoreEffects(List<CardEffectData> effects)
    {
        if (effects == null) return 0;
        double score = 0;
        foreach (CardEffectData effect in effects)
        {
            if (effect == null) continue;
            score += EffectRegistry.Get(effect.effectType).HeuristicScore(effect);
        }
        return score;
    }

    private static double PassiveActionBonus(CardStateSnapshot card)
    {
        double bonus = 0;
        if (card.HasPassive(PassiveType.Guard)) bonus += 5;
        if (card.HasPassive(PassiveType.Rush)) bonus += 7;
        if (card.HasPassive(PassiveType.Charge)) bonus += 7;
        if (card.HasPassive(PassiveType.Swingle)) bonus += 4;
        if (card.HasPassive(PassiveType.Windfury)) bonus += 3;
        if (card.HasPassive(PassiveType.HolyShield)) bonus += 2;
        if (card.HasPassive(PassiveType.Stealth)) bonus += 2;
        if (card.HasPassive(PassiveType.Lifesteal)) bonus += 3;
        if (card.HasPassive(PassiveType.Poisonous)) bonus += 5;
        return bonus;
    }

    private static double ThreatPassiveBonus(CardStateSnapshot card)
    {
        double bonus = 0;
        if (card.HasPassive(PassiveType.Rush)) bonus += 1.5;
        if (card.HasPassive(PassiveType.Charge)) bonus += 1.5;
        if (card.HasPassive(PassiveType.Swingle)) bonus += 2;
        if (card.HasPassive(PassiveType.Windfury)) bonus += 1.5;
        if (card.HasPassive(PassiveType.HolyShield)) bonus += 1;
        if (card.HasPassive(PassiveType.Stealth)) bonus += 1;
        if (card.HasPassive(PassiveType.Lifesteal)) bonus += 4;
        if (card.HasPassive(PassiveType.Poisonous)) bonus += 4;
        return bonus;
    }

    private static bool HasDrawEffect(List<CardEffectData> effects)
    {
        if (effects == null)
        {
            return false;
        }
        foreach (CardEffectData effect in effects)
        {
            if (effect != null && (effect.effectType == EffectType.Draw
                || (effect.effectType == EffectType.DrawCards && effect.targetSide != EffectTargetSide.Enemy)))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsExposedAfterAllInAttack(BattleStateSnapshot state, int playerIndex)
    {
        PlayerStateSnapshot player = state.GetPlayer(playerIndex);
        if (player == null || player.Field.Count == 0)
        {
            return false;
        }

        foreach (CardStateSnapshot card in player.Field)
        {
            if (card != null && card.Health > 0 && card.AttacksRemaining > 0)
            {
                return false;
            }
        }

        int friendlyHealth = 0;
        foreach (CardStateSnapshot card in player.Field)
        {
            if (card != null)
            {
                friendlyHealth += Math.Max(0, card.Health);
            }
        }

        int incomingAttack = 0;
        foreach (PlayerStateSnapshot opponent in state.Players)
        {
            if (opponent.PlayerIndex == playerIndex)
            {
                continue;
            }
            foreach (CardStateSnapshot card in opponent.Field)
            {
                if (card != null && card.Health > 0)
                {
                    incomingAttack += Math.Max(0, card.Attack);
                }
            }
        }
        return incomingAttack >= friendlyHealth;
    }

    public static bool HasAttackableEnemyMinion(BattleStateSnapshot state, CardStateSnapshot source)
    {
        foreach (PlayerStateSnapshot opponent in state.Players)
        {
            if (opponent.PlayerIndex == source.OwnerIndex)
            {
                continue;
            }
            bool hasGuard = HasGuard(opponent);
            foreach (CardStateSnapshot card in opponent.Field)
            {
                if (card == null || card.Health <= 0 || card.IsDying || card.HasPassive(PassiveType.Stealth))
                {
                    continue;
                }
                if (!hasGuard || card.HasPassive(PassiveType.Guard))
                {
                    return true;
                }
            }
        }
        return false;
    }
    private static double RoleBonus(CardAIRole role) =>
        role == CardAIRole.Tempo ? 3
        : role == CardAIRole.Removal ? 4
        : role == CardAIRole.Finisher ? 5
        : role == CardAIRole.Support ? 3
        : role == CardAIRole.Value ? 3
        : 0;
}
