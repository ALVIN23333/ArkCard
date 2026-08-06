using System;
using System.Collections.Generic;
using UnityEngine;

public static class AITargetSelector
{
    public static List<SimulatedTarget> GetCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        return EffectRegistry.Get(effect.effectType).GetSimulationCandidates(state, source, effect);
    }

    public static List<SimulatedTarget> SelectTargets(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        int count,
        List<SimulatedTarget> candidates = null)
    {
        candidates ??= GetCandidates(state, source, effect);
        List<SimulatedTarget> sorted = new(candidates);
        sorted.Sort((left, right) =>
        {
            int scoreComparison = ScoreTarget(state, source, effect, right).CompareTo(ScoreTarget(state, source, effect, left));
            return scoreComparison != 0 ? scoreComparison : CompareTargets(left, right);
        });

        int take = Math.Min(Math.Max(0, count), sorted.Count);
        return sorted.GetRange(0, take);
    }

    public static double ScoreTarget(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, SimulatedTarget target)
    {
        if (state == null || source == null || effect == null || target == null)
        {
            return double.MinValue;
        }

        if (target.Kind == SimulatedTargetKind.Player)
        {
            PlayerStateSnapshot player = state.GetPlayer(target.Id);
            if (player == null)
            {
                return double.MinValue;
            }
            int damage = EffectValues.GetValue(effect, 0);
            int lethalBonus = source.Data != null ? source.Data.aiLethalBonus : 0;
            double score = damage >= player.Health
                ? 1000 + EffectTargetingRules.GetLethalBonus(source)
                : damage * 4 - player.Health * 0.1 + lethalBonus * 2;
            if (source.Data != null && source.Data.aiTargetPriority == AITargetPriority.EnemyHero)
            {
                score += 100;
            }
            return score;
        }

        CardStateSnapshot card = state.FindCard(target.Id);
        if (card == null)
        {
            return double.MinValue;
        }

        double scoreValue = EffectRegistry.Get(effect.effectType).ScoreSimulationTarget(state, source, effect, target);
        return scoreValue + GetMetadataTargetBonus(source, card);
    }

    public static List<UnityEngine.Object> SelectRuntimeTargets(
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> candidates,
        int requiredCount)
    {
        List<UnityEngine.Object> sorted = candidates != null ? new List<UnityEngine.Object>(candidates) : new List<UnityEngine.Object>();
        sorted.RemoveAll(candidate => candidate == null);
        sorted.Sort((left, right) =>
        {
            int comparison = ScoreRuntimeTarget(source, effect, right).CompareTo(ScoreRuntimeTarget(source, effect, left));
            if (comparison != 0)
            {
                return comparison;
            }
            return left.GetInstanceID().CompareTo(right.GetInstanceID());
        });
        int take = Mathf.Min(Mathf.Max(0, requiredCount), sorted.Count);
        return sorted.GetRange(0, take);
    }

    private static double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (source == null || source.cardData == null || effect == null || target == null)
        {
            return double.MinValue;
        }
        if (target is PlayerController player)
        {
            int damage = EffectValues.GetValue(effect, 0);
            double score = damage >= player.health
                ? 1000 + source.cardData.aiLethalBonus
                : damage * 4 - player.health * 0.1 + source.cardData.aiLethalBonus * 2;
            return source.cardData.aiTargetPriority == AITargetPriority.EnemyHero ? score + 100 : score;
        }
        if (target is not CardController card || card.cardData == null)
        {
            return double.MinValue;
        }

        double scoreValue = EffectRegistry.Get(effect.effectType).ScoreRuntimeTarget(source, effect, target);
        return scoreValue + GetRuntimeMetadataTargetBonus(source, card);
    }

    private static double GetMetadataTargetBonus(CardStateSnapshot source, CardStateSnapshot target)
    {
        if (source.Data == null) return 0;
        return source.Data.aiTargetPriority switch
        {
            AITargetPriority.HighAttackEnemy => target.OwnerIndex != source.OwnerIndex ? target.Attack * 3 : 0,
            AITargetPriority.LowHealthEnemy => target.OwnerIndex != source.OwnerIndex ? Math.Max(0, 12 - target.Health) : 0,
            AITargetPriority.GuardFirst => target.OwnerIndex != source.OwnerIndex && target.HasPassive(PassiveType.Guard) ? 20 : 0,
            AITargetPriority.WeakAlly => target.OwnerIndex == source.OwnerIndex ? Math.Max(0, target.MaxHealth - target.Health) * 2 : 0,
            AITargetPriority.StrongAlly => target.OwnerIndex == source.OwnerIndex ? EffectTargetingRules.GetSimulationAllyValue(target) : 0,
            _ => 0,
        };
    }

    private static double GetRuntimeMetadataTargetBonus(CardController source, CardController target)
    {
        bool ally = target.player != null && source.player != null && target.player == source.player;
        return source.cardData.aiTargetPriority switch
        {
            AITargetPriority.HighAttackEnemy => !ally ? target.atk * 3 : 0,
            AITargetPriority.LowHealthEnemy => !ally ? Math.Max(0, 12 - target.health) : 0,
            AITargetPriority.GuardFirst => !ally && target.HasPassive(PassiveType.Guard) ? 20 : 0,
            AITargetPriority.WeakAlly => ally ? Math.Max(0, target.maxHealth - target.health) * 2 : 0,
            AITargetPriority.StrongAlly => ally ? EffectTargetingRules.GetRuntimeAllyValue(target) : 0,
            _ => 0,
        };
    }

    private static int CompareTargets(SimulatedTarget left, SimulatedTarget right)
    {
        int kind = left.Kind.CompareTo(right.Kind);
        return kind != 0 ? kind : left.Id.CompareTo(right.Id);
    }
}
