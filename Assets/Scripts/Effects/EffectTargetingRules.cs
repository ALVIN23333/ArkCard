using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 目标候选与评分共享规则：运行时与 AI 模拟共用，保证两侧过滤/评分一致。
/// </summary>
public static class EffectTargetingRules
{
    public static List<UnityEngine.Object> GetConfiguredCharacters(
        CardController source,
        CardEffectData effect,
        bool applySelectionRestrictions)
    {
        List<UnityEngine.Object> targets = new();
        if (source == null || effect == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return targets;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || !MatchesSide(player == source.player, effect.targetSide))
            {
                continue;
            }

            if (effect.characterScope != EffectCharacterScope.Minions)
            {
                targets.Add(player);
            }

            if (effect.characterScope == EffectCharacterScope.Heroes || player.fieldController == null)
            {
                continue;
            }

            foreach (CardController card in player.fieldController.fieldCards)
            {
                if (card == null || (!effect.includeSource && card == source))
                {
                    continue;
                }

                bool enemy = card.player != source.player;
                if (applySelectionRestrictions
                    && ((enemy && card.isStealth) || IsMagicImmuneToSource(source, card)))
                {
                    continue;
                }

                targets.Add(card);
            }
        }

        return targets;
    }

    public static List<SimulatedTarget> GetConfiguredCharacters(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        bool applySelectionRestrictions)
    {
        List<SimulatedTarget> targets = new();
        if (state == null || source == null || effect == null)
        {
            return targets;
        }

        bool spellSource = source.Data != null && source.Data.cardType == CardType.SPELL;
        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player == null || !MatchesSide(player.PlayerIndex == source.OwnerIndex, effect.targetSide))
            {
                continue;
            }

            if (effect.characterScope != EffectCharacterScope.Minions)
            {
                targets.Add(SimulatedTarget.Player(player.PlayerIndex));
            }

            if (effect.characterScope == EffectCharacterScope.Heroes)
            {
                continue;
            }

            foreach (CardStateSnapshot card in player.Field)
            {
                if (card == null || (!effect.includeSource && card.RuntimeId == source.RuntimeId))
                {
                    continue;
                }

                bool enemy = card.OwnerIndex != source.OwnerIndex;
                if (applySelectionRestrictions
                    && ((enemy && card.IsStealth) || (spellSource && card.HasPassive(PassiveType.MagicImmunity))))
                {
                    continue;
                }

                targets.Add(SimulatedTarget.Card(card.RuntimeId));
            }
        }

        return targets;
    }

    public static List<UnityEngine.Object> GetConfiguredGraveyardMinions(CardController source, EffectTargetSide side)
    {
        List<UnityEngine.Object> targets = new();
        if (source == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return targets;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || !MatchesSide(player == source.player, side) || player.graveCards == null)
            {
                continue;
            }

            foreach (CardController card in player.graveCards)
            {
                if (card != null && card.cardData != null && card.cardData.cardType == CardType.Minion)
                {
                    targets.Add(card);
                }
            }
        }

        return targets;
    }

    public static List<SimulatedTarget> GetConfiguredGraveyardMinions(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        EffectTargetSide side)
    {
        List<SimulatedTarget> targets = new();
        if (state == null || source == null)
        {
            return targets;
        }

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player == null || !MatchesSide(player.PlayerIndex == source.OwnerIndex, side))
            {
                continue;
            }

            foreach (CardStateSnapshot card in player.Graveyard)
            {
                if (card != null && card.Data != null && card.Data.cardType == CardType.Minion)
                {
                    targets.Add(SimulatedTarget.Card(card.RuntimeId));
                }
            }
        }

        return targets;
    }

    public static bool MatchesSide(bool friendly, EffectTargetSide side)
    {
        return side == EffectTargetSide.Both
            || (side == EffectTargetSide.Friendly && friendly)
            || (side == EffectTargetSide.Enemy && !friendly);
    }

    // ---------------- 运行时候选 ----------------

    public static List<UnityEngine.Object> GetEnemyCharacters(CardController source)
    {
        List<UnityEngine.Object> targets = GetEnemyField(source);
        if (source == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return targets;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player != null && player != source.player)
            {
                targets.Add(player);
            }
        }

        return targets;
    }

    public static List<UnityEngine.Object> GetEnemyField(CardController source)
    {
        List<UnityEngine.Object> targets = new();
        if (source == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return targets;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player == source.player || player.fieldController == null)
            {
                continue;
            }

            foreach (CardController card in player.fieldController.fieldCards)
            {
                if (card != null && !card.isStealth && !IsMagicImmuneToSource(source, card))
                {
                    targets.Add(card);
                }
            }
        }

        return targets;
    }

    public static List<UnityEngine.Object> GetAllyField(CardController source)
    {
        List<UnityEngine.Object> targets = new();
        if (source == null || source.player == null || source.player.fieldController == null)
        {
            return targets;
        }

        foreach (CardController card in source.player.fieldController.fieldCards)
        {
            if (card != null && !IsMagicImmuneToSource(source, card))
            {
                targets.Add(card);
            }
        }

        return targets;
    }

    public static List<UnityEngine.Object> GetAllField()
    {
        List<UnityEngine.Object> targets = new();
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            return targets;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player.fieldController == null)
            {
                continue;
            }

            foreach (CardController card in player.fieldController.fieldCards)
            {
                if (card != null)
                {
                    targets.Add(card);
                }
            }
        }

        return targets;
    }

    public static List<UnityEngine.Object> GetOtherField(CardController source)
    {
        List<UnityEngine.Object> targets = GetAllField();
        if (source != null)
        {
            targets.RemoveAll(target =>
                target == source
                || (target is CardController targetCard
                    && source.player != null
                    && targetCard.player != null
                    && targetCard.player != source.player
                    && targetCard.isStealth)
                || (target is CardController magicImmuneCard && IsMagicImmuneToSource(source, magicImmuneCard)));
        }

        return targets;
    }

    public static List<UnityEngine.Object> GetAllyGraveyardMinions(PlayerController sourcePlayer)
    {
        List<UnityEngine.Object> targets = new();
        if (sourcePlayer == null || sourcePlayer.graveCards == null)
        {
            return targets;
        }

        foreach (CardController card in sourcePlayer.graveCards)
        {
            if (card != null
                && card.player == sourcePlayer
                && card.state == CardState.Graveyard
                && card.cardData != null
                && card.cardData.cardType == CardType.Minion)
            {
                targets.Add(card);
            }
        }

        return targets;
    }

    public static bool IsMagicImmuneToSource(CardController source, CardController target)
    {
        if (source == null || target == null || target.cardData == null)
        {
            return false;
        }

        return source.cardData != null
            && source.cardData.cardType == CardType.SPELL
            && target.HasPassive(PassiveType.MagicImmunity);
    }

    // ---------------- AI 模拟候选 ----------------

    public static List<SimulatedTarget> GetEnemyCharacters(BattleStateSnapshot state, int ownerIndex, bool spellSource)
    {
        List<SimulatedTarget> targets = GetEnemyField(state, ownerIndex, spellSource);
        if (state == null)
        {
            return targets;
        }

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player != null && player.PlayerIndex != ownerIndex)
            {
                targets.Add(SimulatedTarget.Player(player.PlayerIndex));
            }
        }

        return targets;
    }

    public static List<SimulatedTarget> GetEnemyField(BattleStateSnapshot state, int ownerIndex, bool spellSource)
    {
        List<SimulatedTarget> targets = new();
        if (state == null)
        {
            return targets;
        }

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player == null || player.PlayerIndex == ownerIndex)
            {
                continue;
            }

            AddCardTargets(player.Field, targets, 0, true, true, spellSource);
        }

        return targets;
    }

    public static List<SimulatedTarget> GetAllyField(BattleStateSnapshot state, int ownerIndex, bool spellSource)
    {
        List<SimulatedTarget> targets = new();
        PlayerStateSnapshot owner = state != null ? state.GetPlayer(ownerIndex) : null;
        if (owner != null)
        {
            AddCardTargets(owner.Field, targets, 0, false, false, spellSource);
        }

        return targets;
    }

    public static List<SimulatedTarget> GetAllField(BattleStateSnapshot state)
    {
        List<SimulatedTarget> targets = new();
        if (state == null)
        {
            return targets;
        }

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player != null)
            {
                AddCardTargets(player.Field, targets, 0, false, false, false);
            }
        }

        return targets;
    }

    public static List<SimulatedTarget> GetOtherField(BattleStateSnapshot state, CardStateSnapshot source)
    {
        List<SimulatedTarget> targets = new();
        if (state == null || source == null)
        {
            return targets;
        }

        bool spellSource = source.Data != null && source.Data.cardType == CardType.SPELL;
        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player != null)
            {
                AddCardTargets(
                    player.Field,
                    targets,
                    source.RuntimeId,
                    true,
                    player.PlayerIndex != source.OwnerIndex,
                    spellSource);
            }
        }

        return targets;
    }

    public static List<SimulatedTarget> GetAllyGraveyardMinions(BattleStateSnapshot state, int ownerIndex)
    {
        List<SimulatedTarget> targets = new();
        PlayerStateSnapshot owner = state != null ? state.GetPlayer(ownerIndex) : null;
        if (owner != null)
        {
            foreach (CardStateSnapshot card in owner.Graveyard)
            {
                if (card != null && card.Data != null && card.Data.cardType == CardType.Minion)
                {
                    targets.Add(SimulatedTarget.Card(card.RuntimeId));
                }
            }
        }

        return targets;
    }

    public static void AddCardTargets(
        List<CardStateSnapshot> cards,
        List<SimulatedTarget> targets,
        int excludedId,
        bool excludeSource,
        bool skipStealth,
        bool skipMagicImmune)
    {
        if (cards == null)
        {
            return;
        }

        foreach (CardStateSnapshot card in cards)
        {
            if (card != null
                && (!excludeSource || card.RuntimeId != excludedId)
                && (!skipStealth || !card.IsStealth)
                && (!skipMagicImmune || !card.HasPassive(PassiveType.MagicImmunity)))
            {
                targets.Add(SimulatedTarget.Card(card.RuntimeId));
            }
        }
    }

    // ---------------- 评分工具 ----------------

    public static double GetSimulationThreat(CardStateSnapshot card)
    {
        return card == null
            ? double.MinValue
            : 2 * card.Attack + card.Health + GetSimulationPassiveBonus(card) + GetSimulationBuffAmount(card);
    }

    public static double GetSimulationAllyValue(CardStateSnapshot card)
    {
        return card == null
            ? double.MinValue
            : 2 * card.Attack + card.MaxHealth + GetSimulationPassiveBonus(card) + CountUsefulEffects(card) * 2;
    }

    public static double GetRuntimeThreat(CardController card)
    {
        return card == null || card.cardData == null
            ? double.MinValue
            : 2 * card.atk + card.health + GetRuntimePassiveBonus(card) + GetRuntimeBuffAmount(card);
    }

    public static double GetRuntimeAllyValue(CardController card)
    {
        return card == null || card.cardData == null
            ? double.MinValue
            : 2 * card.atk + card.maxHealth + GetRuntimePassiveBonus(card) + CountUsefulEffects(card.cardData) * 2;
    }

    public static int GetSimulationPassiveBonus(CardStateSnapshot card)
    {
        int bonus = 0;
        if (card == null)
        {
            return bonus;
        }

        if (card.HasPassive(PassiveType.Guard)) bonus += 4;
        if (card.HasPassive(PassiveType.Rush)) bonus += 2;
        if (card.HasPassive(PassiveType.Charge)) bonus += 3;
        if (card.HasPassive(PassiveType.Swingle)) bonus += 3;
        if (card.HasPassive(PassiveType.Windfury)) bonus += 2;
        if (card.HasPassive(PassiveType.HolyShield)) bonus += 2;
        if (card.HasPassive(PassiveType.Stealth)) bonus += 2;
        if (card.HasPassive(PassiveType.Lifesteal)) bonus += 4;
        if (card.HasPassive(PassiveType.Poisonous)) bonus += 4;
        return bonus;
    }

    public static int GetSimulationBuffAmount(CardStateSnapshot card)
    {
        return card == null || card.Data == null
            ? 0
            : Math.Max(0, card.Attack - card.Data.attack) + Math.Max(0, card.MaxHealth - card.Data.health);
    }

    public static int GetRuntimePassiveBonus(CardController card)
    {
        int bonus = 0;
        if (card == null)
        {
            return bonus;
        }

        if (card.HasPassive(PassiveType.Guard)) bonus += 4;
        if (card.HasPassive(PassiveType.Rush)) bonus += 2;
        if (card.HasPassive(PassiveType.Charge)) bonus += 3;
        if (card.HasPassive(PassiveType.Swingle)) bonus += 3;
        if (card.HasPassive(PassiveType.Windfury)) bonus += 2;
        if (card.HasPassive(PassiveType.HolyShield)) bonus += 2;
        if (card.HasPassive(PassiveType.Stealth)) bonus += 2;
        if (card.HasPassive(PassiveType.Lifesteal)) bonus += 4;
        if (card.HasPassive(PassiveType.Poisonous)) bonus += 4;
        return bonus;
    }

    public static int GetRuntimeBuffAmount(CardController card)
    {
        return card == null || card.cardData == null
            ? 0
            : Math.Max(0, card.atk - card.cardData.attack) + Math.Max(0, card.maxHealth - card.cardData.health);
    }

    public static int CountUsefulEffects(CardStateSnapshot card)
    {
        return card == null || card.Data == null ? 0 : CountUsefulEffects(card.Data);
    }

    public static int CountUsefulEffects(CardData data)
    {
        return data == null || data.effects == null ? 0 : data.effects.Count;
    }

    public static int GetLethalBonus(CardStateSnapshot source)
    {
        return source != null && source.Data != null ? source.Data.aiLethalBonus : 0;
    }
}
