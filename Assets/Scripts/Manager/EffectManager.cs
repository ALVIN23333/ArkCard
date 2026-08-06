using System;
using System.Collections.Generic;
using UnityEngine;

public class EffectManager : MonoBehaviour
{
    private readonly Queue<PendingTrigger> pendingTriggers = new();
    private bool isProcessingTriggerQueue;

    public bool IsProcessingEffects => isProcessingTriggerQueue || pendingTriggers.Count > 0;

    private sealed class PendingTrigger
    {
        public CardController source;
        public TriggerType triggerType;
        public bool executeAllEffects;
        public bool hangSourceDuringProcessing;
        public List<UnityEngine.Object> selectedTargets;
        public Action onComplete;
    }

    public void Init()
    {
        pendingTriggers.Clear();
        isProcessingTriggerQueue = false;
    }

    public void TriggerCardEffect(CardController card, TriggerType triggerType, List<UnityEngine.Object> selectedTargets = null, Action onComplete = null)
    {
        if (card == null || card.cardData == null || card.isSilence)
        {
            onComplete?.Invoke();
            return;
        }

        if (card.cardData.cardType == CardType.SPELL)
        {
            TriggerSpellEffect(card, selectedTargets, onComplete);
            return;
        }

        EnqueueTrigger(card, triggerType, false, selectedTargets, onComplete);
    }

    public void TriggerSpellEffect(CardController card, List<UnityEngine.Object> selectedTargets = null, Action onComplete = null)
    {
        if (card == null || card.cardData == null || card.isSilence || card.cardData.cardType != CardType.SPELL)
        {
            onComplete?.Invoke();
            return;
        }

        EnqueueTrigger(card, TriggerType.None, true, selectedTargets, onComplete);
    }

    public void TriggerDeathEffect(CardController card, Action onComplete = null)
    {
        if (card == null || card.cardData == null || card.isSilence)
        {
            onComplete?.Invoke();
            return;
        }

        List<CardEffectData> matchingEffects = GetMatchingEffects(card, TriggerType.Died, false);
        if (!HasEffects(matchingEffects))
        {
            onComplete?.Invoke();
            return;
        }

        EnqueueTrigger(card, TriggerType.Died, false, null, onComplete, true);
    }

    private void EnqueueTrigger(
        CardController card,
        TriggerType triggerType,
        bool executeAllEffects,
        List<UnityEngine.Object> selectedTargets,
        Action onComplete,
        bool hangSourceDuringProcessing = false)
    {
        pendingTriggers.Enqueue(new PendingTrigger
        {
            source = card,
            triggerType = triggerType,
            executeAllEffects = executeAllEffects,
            hangSourceDuringProcessing = hangSourceDuringProcessing,
            selectedTargets = selectedTargets != null ? new List<UnityEngine.Object>(selectedTargets) : null,
            onComplete = onComplete,
        });

        if (!isProcessingTriggerQueue)
        {
            ProcessNextPendingTrigger();
        }
    }

    public void TriggerFieldEffects(PlayerController player, TriggerType triggerType)
    {
        if (player == null || player.fieldController == null)
        {
            return;
        }

        List<CardController> cards = new(player.fieldController.fieldCards);
        foreach (CardController card in cards)
        {
            if (card == null)
            {
                continue;
            }

            TriggerCardEffect(card, triggerType);
        }
    }

    private void ProcessNextPendingTrigger()
    {
        while (pendingTriggers.Count > 0)
        {
            PendingTrigger trigger = pendingTriggers.Dequeue();
            if (trigger.source == null || trigger.source.cardData == null)
            {
                trigger.onComplete?.Invoke();
                continue;
            }

            isProcessingTriggerQueue = true;
            List<CardEffectData> matchingEffects = GetMatchingEffects(trigger.source, trigger.triggerType, trigger.executeAllEffects);
            if (!HasEffects(matchingEffects))
            {
                trigger.onComplete?.Invoke();
                continue;
            }

            TargetManager targetManager = GM.Ins != null && GM.Ins.BM != null ? GM.Ins.BM.TM : null;
            if (trigger.hangSourceDuringProcessing)
            {
                targetManager?.EnterHangingState(trigger.source, false);
            }

            AnimeManager.PlayTriggerAnimation(trigger.source, () =>
            {
                CardEffectContext context = new(trigger.triggerType == TriggerType.Died);
                ExecuteEffectList(trigger.source, matchingEffects, trigger.selectedTargets, context, () =>
                {
                    void CompleteTrigger()
                    {
                        trigger.onComplete?.Invoke();
                        ProcessNextPendingTrigger();
                    }

                    if (trigger.hangSourceDuringProcessing && targetManager != null)
                    {
                        targetManager.ReleaseHangingState(trigger.source, CompleteTrigger);
                        return;
                    }

                    CompleteTrigger();
                });
            });
            return;
        }

        isProcessingTriggerQueue = false;
        if (GM.Ins != null && GM.Ins.BM != null)
        {
            RefreshAllFields(GM.Ins.BM);
            GM.Ins.BM.CheckGameOver();
        }
    }

    private List<CardEffectData> GetMatchingEffects(CardController source, TriggerType triggerType, bool executeAllEffects)
    {
        List<CardEffectData> matchingEffects = new();
        if (source == null || source.cardData == null || source.cardData.effects == null)
        {
            return matchingEffects;
        }

        foreach (CardEffectData effect in source.cardData.effects)
        {
            if (effect != null && (executeAllEffects || effect.triggerType == triggerType))
            {
                matchingEffects.Add(effect);
            }
        }

        return matchingEffects;
    }

    private void ExecuteEffects(CardController source, TriggerType triggerType, bool executeAllEffects, List<UnityEngine.Object> selectedTargets, Action onComplete)
    {
        if (source.cardData.effects == null || source.cardData.effects.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        List<CardEffectData> matchingEffects = new();
        foreach (CardEffectData effect in source.cardData.effects)
        {
            if (effect != null && (executeAllEffects || effect.triggerType == triggerType))
            {
                matchingEffects.Add(effect);
            }
        }

        ExecuteEffectList(source, matchingEffects, selectedTargets, new CardEffectContext(false), onComplete);
    }

    private void ExecuteEffectList(
        CardController source,
        List<CardEffectData> effects,
        List<UnityEngine.Object> selectedTargets,
        CardEffectContext context,
        Action onComplete)
    {
        if (!HasEffects(effects))
        {
            onComplete?.Invoke();
            return;
        }

        ExecuteEffectListAtIndex(source, effects, 0, selectedTargets, context, onComplete);
    }

    private void ExecuteEffectListAtIndex(
        CardController source,
        List<CardEffectData> effects,
        int index,
        List<UnityEngine.Object> selectedTargets,
        CardEffectContext context,
        Action onComplete)
    {
        if (context == null || context.IsCancelled || !HasEffects(effects) || index >= effects.Count)
        {
            onComplete?.Invoke();
            return;
        }

        CardEffectData effect = effects[index];
        if (effect == null)
        {
            ExecuteEffectListAtIndex(source, effects, index + 1, selectedTargets, context, onComplete);
            return;
        }

        ResolveEffect(
            source,
            effect,
            selectedTargets,
            context,
            () => ExecuteEffectListAtIndex(source, effects, index + 1, selectedTargets, context, onComplete));
    }

    private void ResolveEffect(
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> selectedTargets,
        CardEffectContext context,
        Action onComplete)
    {
        if (effect == null)
        {
            onComplete?.Invoke();
            return;
        }

        bool passed = CheckConditions(source, effect.conditionTypes);
        bool hasBranches = HasEffects(effect.thenEffects) || HasEffects(effect.elseEffects);
        bool hasConditions = HasConditions(effect.conditionTypes);

        if (hasConditions && hasBranches)
        {
            ExecuteEffectList(source, passed ? effect.thenEffects : effect.elseEffects, selectedTargets, context, onComplete);
            return;
        }

        if (hasConditions && !passed)
        {
            onComplete?.Invoke();
            return;
        }

        ExecuteSingleEffect(source, effect, selectedTargets, context, onComplete);
    }

    private void ExecuteSingleEffect(
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> selectedTargets,
        CardEffectContext context,
        Action onComplete)
    {
        ICardEffectDefinition definition = EffectRegistry.Get(effect.effectType);
        if (definition.EffectType != EffectType.None && !definition.IsTargeted)
        {
            context.CommitEffect();
        }

        if (!definition.IsTargeted)
        {
            definition.ApplyRuntime(context, source, effect, null, onComplete);
            return;
        }

        ResolveSelectedTargets(
            source,
            effect,
            definition.GetRuntimeCandidates(source, effect),
            definition.GetRuntimeSelectionCount(source, effect),
            definition.SelectionZone,
            selectedTargets,
            context,
            targets => definition.ApplyRuntime(context, source, effect, targets, onComplete));
    }

    public bool HasRequiredCondition(CardController card, CardEffectData effect)
    {
        if (effect == null)
        {
            return false;
        }

        if (!HasConditions(effect.conditionTypes))
        {
            return true;
        }

        if (HasEffects(effect.elseEffects))
        {
            return true;
        }

        return CheckConditions(card, effect.conditionTypes);
    }

    public bool CheckConditions(CardController source, List<ConditionType> conditionTypes)
    {
        if (!HasConditions(conditionTypes))
        {
            return true;
        }

        foreach (ConditionType conditionType in conditionTypes)
        {
            if (conditionType == ConditionType.None)
            {
                continue;
            }

            if (!CheckCondition(source, conditionType))
            {
                return false;
            }
        }

        return true;
    }

    public bool CheckCondition(CardController source, ConditionType conditionType)
    {
        if (source == null || source.player == null)
        {
            return false;
        }

        switch (conditionType)
        {
            case ConditionType.None:
                return true;
            case ConditionType.ThreeMoreHand:
                return source.player.handController != null && source.player.handController.handCards.Count >= 3;
            case ConditionType.HasEnemy:
                return HasEnemyMinion(source.player);
            case ConditionType.HasOther:
                return HasOtherAllyMinion(source);
            case ConditionType.HasAlly:
                return source.player.fieldController != null && source.player.fieldController.fieldCards.Count > 0;
            case ConditionType.HasDiedAlly:
                return source.player.graveCards.Count > 0;
            case ConditionType.HasDiedEnemy:
                return HasDiedEnemyMinion(source.player);
            case ConditionType.HasEmptyField:
                return source.player.fieldController != null
                    && source.player.fieldController.fieldCards.Count < GameConst.fieldMax;
            case ConditionType.HasNonMagicalImmunityAlly:
                return HasNonMagicalImmunityAllyMinion(source);
            case ConditionType.HasNonMagicalImmunityEnemy:
                return HasNonMagicalImmunityEnemyMinion(source.player);
            case ConditionType.HasNonMagicalImmunityOther:
                return HasNonMagicalImmunityOtherMinion(source);
            default:
                return false;
        }
    }

    private bool HasEnemyMinion(PlayerController player)
    {
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            return false;
        }

        foreach (PlayerController otherPlayer in GM.Ins.BM.players)
        {
            if (otherPlayer == null || otherPlayer == player || otherPlayer.fieldController == null)
            {
                continue;
            }

            if (otherPlayer.fieldController.fieldCards.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasOtherAllyMinion(CardController source)
    {
        if (source == null || source.player == null || source.player.fieldController == null)
        {
            return false;
        }

        foreach (CardController card in source.player.fieldController.fieldCards)
        {
            if (card != null && card != source)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasDiedEnemyMinion(PlayerController player)
    {
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            return false;
        }

        foreach (PlayerController otherPlayer in GM.Ins.BM.players)
        {
            if (otherPlayer != null
                && otherPlayer != player
                && otherPlayer.graveCards != null
                && otherPlayer.graveCards.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNonMagicalImmunityAllyMinion(CardController source)
    {
        if (source == null || source.player == null || source.player.fieldController == null)
        {
            return false;
        }

        foreach (CardController card in source.player.fieldController.fieldCards)
        {
            if (card != null && !card.HasPassive(PassiveType.MagicImmunity))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasNonMagicalImmunityEnemyMinion(PlayerController player)
    {
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            return false;
        }

        foreach (PlayerController otherPlayer in GM.Ins.BM.players)
        {
            if (otherPlayer == null || otherPlayer == player || otherPlayer.fieldController == null)
            {
                continue;
            }

            foreach (CardController card in otherPlayer.fieldController.fieldCards)
            {
                if (card != null && !card.HasPassive(PassiveType.MagicImmunity))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasNonMagicalImmunityOtherMinion(CardController source)
    {
        if (source == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return false;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player.fieldController == null)
            {
                continue;
            }

            foreach (CardController card in player.fieldController.fieldCards)
            {
                if (card != null && card != source && !card.HasPassive(PassiveType.MagicImmunity))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasEffects(List<CardEffectData> effects)
    {
        return effects != null && effects.Count > 0;
    }

    private static bool HasConditions(List<ConditionType> conditionTypes)
    {
        if (conditionTypes == null || conditionTypes.Count == 0)
        {
            return false;
        }

        foreach (ConditionType conditionType in conditionTypes)
        {
            if (conditionType != ConditionType.None)
            {
                return true;
            }
        }

        return false;
    }

    private void ResolveSelectedTargets(
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> candidates,
        int requiredCount,
        TargetSelectionZone zone,
        List<UnityEngine.Object> selectedTargets,
        CardEffectContext context,
        Action<List<UnityEngine.Object>> onResolved)
    {
        void CompleteWithTargets(List<UnityEngine.Object> targets)
        {
            if (targets != null && targets.Count > 0)
            {
                context.CommitEffect();
            }

            onResolved?.Invoke(targets);
        }

        if (requiredCount <= 0)
        {
            onResolved?.Invoke(new List<UnityEngine.Object>());
            return;
        }

        if (candidates == null || candidates.Count == 0)
        {
            onResolved?.Invoke(new List<UnityEngine.Object>());
            return;
        }

        int resolvedRequiredCount = Mathf.Min(Mathf.Max(1, requiredCount), candidates.Count);
        List<UnityEngine.Object> resolvedTargets = new();
        if (selectedTargets != null)
        {
            foreach (UnityEngine.Object selectedTarget in selectedTargets)
            {
                if (selectedTarget != null && candidates.Contains(selectedTarget) && !resolvedTargets.Contains(selectedTarget))
                {
                    resolvedTargets.Add(selectedTarget);
                    if (resolvedTargets.Count >= resolvedRequiredCount)
                    {
                        CompleteWithTargets(resolvedTargets);
                        return;
                    }
                }
            }
        }

        List<UnityEngine.Object> remainingCandidates = new(candidates);
        remainingCandidates.RemoveAll(candidate => resolvedTargets.Contains(candidate));
        int remainingRequiredCount = resolvedRequiredCount - resolvedTargets.Count;
        TargetSelectionRequest request = new()
        {
            sourceCard = source,
            candidates = remainingCandidates,
            requiredCount = remainingRequiredCount,
            zone = zone,
        };

        bool shouldAutoResolve =
            source == null
            || source.player == null
            || source.player.IsAIControlled
            || !source.player.isMainPlayer
            || GM.Ins == null
            || GM.Ins.BM == null
            || GM.Ins.BM.TM == null;

        if (shouldAutoResolve)
        {
            resolvedTargets.AddRange(AITargetSelector.SelectRuntimeTargets(
                source,
                effect,
                remainingCandidates,
                remainingRequiredCount));
            CompleteWithTargets(resolvedTargets);
            return;
        }

        bool selectionStarted = GM.Ins.BM.TM.SelectTargets(
            source,
            request.candidates,
            request.requiredCount,
            zone,
            targets =>
            {
                resolvedTargets.AddRange(targets);
                CompleteWithTargets(resolvedTargets);
            },
            () =>
            {
                context.Cancel();
                onResolved?.Invoke(new List<UnityEngine.Object>());
            },
            context.AllowRollback);

        if (!selectionStarted)
        {
            onResolved?.Invoke(new List<UnityEngine.Object>());
        }
    }

    private static void RefreshAllFields(BattleManager battleManager)
    {
        if (battleManager == null || battleManager.players == null)
        {
            return;
        }

        foreach (PlayerController player in battleManager.players)
        {
            player?.fieldController?.RefreshField();
        }
    }
}
