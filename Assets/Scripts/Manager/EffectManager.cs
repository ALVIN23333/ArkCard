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
                ExecuteEffectList(trigger.source, matchingEffects, trigger.selectedTargets, () =>
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

        ExecuteEffectList(source, matchingEffects, selectedTargets, onComplete);
    }

    private void ExecuteEffectList(CardController source, List<CardEffectData> effects, List<UnityEngine.Object> selectedTargets, Action onComplete)
    {
        if (!HasEffects(effects))
        {
            onComplete?.Invoke();
            return;
        }

        ExecuteEffectListAtIndex(source, effects, 0, selectedTargets, onComplete);
    }

    private void ExecuteEffectListAtIndex(CardController source, List<CardEffectData> effects, int index, List<UnityEngine.Object> selectedTargets, Action onComplete)
    {
        if (!HasEffects(effects) || index >= effects.Count)
        {
            onComplete?.Invoke();
            return;
        }

        CardEffectData effect = effects[index];
        if (effect == null)
        {
            ExecuteEffectListAtIndex(source, effects, index + 1, selectedTargets, onComplete);
            return;
        }

        ResolveEffect(
            source,
            effect,
            selectedTargets,
            () => ExecuteEffectListAtIndex(source, effects, index + 1, selectedTargets, onComplete));
    }

    private void ResolveEffect(CardController source, CardEffectData effect, List<UnityEngine.Object> selectedTargets, Action onComplete)
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
            ExecuteEffectList(source, passed ? effect.thenEffects : effect.elseEffects, selectedTargets, onComplete);
            return;
        }

        if (hasConditions && !passed)
        {
            onComplete?.Invoke();
            return;
        }

        ExecuteSingleEffect(source, effect, selectedTargets, onComplete);
    }

    private void ExecuteSingleEffect(CardController source, CardEffectData effect, List<UnityEngine.Object> selectedTargets, Action onComplete)
    {
        switch (effect.effectType)
        {
            case EffectType.Draw:
                GM.Ins.BM.DrawCard(source.player, GetEffectValue(effect, 0));
                onComplete?.Invoke();
                break;
            case EffectType.BuffSelf:
                source.AddStats(GetEffectValue(effect, 0), GetEffectValue(effect, 1));
                onComplete?.Invoke();
                break;
            case EffectType.BuffAlliesAll:
                BuffAllies(source, GetEffectValue(effect, 0), GetEffectValue(effect, 1));
                onComplete?.Invoke();
                break;
            case EffectType.BuffAllEnemies:
                BuffEnemies(source, GetEffectValue(effect, 0), GetEffectValue(effect, 1));
                onComplete?.Invoke();
                break;
            case EffectType.healAlliesAll:
                HealAllies(source, GetEffectValue(effect, 0));
                onComplete?.Invoke();
                break;
            case EffectType.DamageAll:
                DamageCharacters(source, GetEffectValue(effect, 0), false);
                onComplete?.Invoke();
                break;
            case EffectType.DamageAllEnemy:
                DamageCharacters(source, GetEffectValue(effect, 0), true);
                onComplete?.Invoke();
                break;
            case EffectType.AddCostMax:
                source.player.AddMaxCost(GetEffectValue(effect, 0));
                onComplete?.Invoke();
                break;
            case EffectType.AddCost:
                source.player.AddCost(GetEffectValue(effect, 0));
                onComplete?.Invoke();
                break;
            case EffectType.AddBothCost:
                AddCostAndMaxCost(source.player, GetEffectValue(effect, 0));
                onComplete?.Invoke();
                break;
            case EffectType.DisCard:
                DiscardRandomCards(source.player, GetEffectValue(effect, 0));
                onComplete?.Invoke();
                break;
            case EffectType.DealDamageToEnemy:
                ResolveSelectedTargets(source, GetEnemyCharacterTargets(source.player), GetSelectionCount(effect), TargetSelectionZone.Field, selectedTargets, targets =>
                {
                    DamageSelectedTargets(targets, GetEffectValue(effect, 0));
                    onComplete?.Invoke();
                });
                break;
            case EffectType.AllyBackHand:
                ResolveSelectedTargets(source, GetAllyFieldTargets(source.player), GetSelectionCount(effect), TargetSelectionZone.Field, selectedTargets, targets =>
                {
                    ReturnSelectedTargetsToOwnerHand(targets);
                    onComplete?.Invoke();
                });
                break;
            case EffectType.EnemyBackHand:
                ResolveSelectedTargets(source, GetEnemyFieldTargets(source.player), GetSelectionCount(effect), TargetSelectionZone.Field, selectedTargets, targets =>
                {
                    ReturnSelectedTargetsToOwnerHand(targets);
                    onComplete?.Invoke();
                });
                break;
            case EffectType.OtherBackHand:
                ResolveSelectedTargets(source, GetOtherFieldTargets(source), GetSelectionCount(effect), TargetSelectionZone.Field, selectedTargets, targets =>
                {
                    ReturnSelectedTargetsToOwnerHand(targets);
                    onComplete?.Invoke();
                });
                break;
            case EffectType.BuffEnemy:
                ResolveSelectedTargets(source, GetEnemyFieldTargets(source.player), GetSelectionCount(effect), TargetSelectionZone.Field, selectedTargets, targets =>
                {
                    BuffSelectedTargets(targets, GetEffectValue(effect, 0), GetEffectValue(effect, 1));
                    onComplete?.Invoke();
                });
                break;
            case EffectType.BuffAlly:
                ResolveSelectedTargets(source, GetAllyFieldTargets(source.player), GetSelectionCount(effect), TargetSelectionZone.Field, selectedTargets, targets =>
                {
                    BuffSelectedTargets(targets, GetEffectValue(effect, 0), GetEffectValue(effect, 1));
                    onComplete?.Invoke();
                });
                break;
            case EffectType.SlienceEnemy:
                ResolveSelectedTargets(source, GetEnemyFieldTargets(source.player), GetSelectionCount(effect), TargetSelectionZone.Field, selectedTargets, targets =>
                {
                    SilenceSelectedTargets(targets);
                    onComplete?.Invoke();
                });
                break;
            case EffectType.DestoryEnemy:
                ResolveSelectedTargets(source, GetEnemyFieldTargets(source.player), GetSelectionCount(effect), TargetSelectionZone.Field, selectedTargets, targets =>
                {
                    DestroySelectedTargets(targets);
                    onComplete?.Invoke();
                });
                break;
            case EffectType.HealAlly:
                ResolveSelectedTargets(source, GetAllyFieldTargets(source.player), GetSelectionCount(effect), TargetSelectionZone.Field, selectedTargets, targets =>
                {
                    HealSelectedTargets(targets, GetEffectValue(effect, 0));
                    onComplete?.Invoke();
                });
                break;
            case EffectType.ReviveAlly:
                ResolveSelectedTargets(source, GetAllyGraveyardMinionTargets(source.player), GetReviveSelectionCount(source, effect), TargetSelectionZone.Graveyard, selectedTargets, targets =>
                {
                    ReviveSelectedAllies(source.player, targets);
                    onComplete?.Invoke();
                });
                break;
            default:
                onComplete?.Invoke();
                break;
        }
    }
    private void BuffAllies(CardController source, int attackValue, int healthValue)
    {
        if (source == null || source.player == null || source.player.fieldController == null)
        {
            return;
        }

        List<CardController> allies = new(source.player.fieldController.fieldCards);
        foreach (CardController ally in allies)
        {
            if (ally != null)
            {
                ally.AddStats(attackValue, healthValue);
            }
        }
    }

    private void BuffEnemies(CardController source, int attackValue, int healthValue)
    {
        if (source == null || source.player == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player == source.player || player.fieldController == null)
            {
                continue;
            }

            List<CardController> enemies = new(player.fieldController.fieldCards);
            foreach (CardController enemy in enemies)
            {
                if (enemy != null)
                {
                    enemy.AddStats(attackValue, healthValue);
                }
            }
        }
    }

    private void HealAllies(CardController source, int healValue)
    {
        if (source == null || source.player == null || healValue <= 0)
        {
            return;
        }

        source.player.Heal(healValue);
        if (source.player.fieldController == null)
        {
            return;
        }

        List<CardController> allies = new(source.player.fieldController.fieldCards);
        foreach (CardController ally in allies)
        {
            if (ally != null)
            {
                ally.Heal(healValue);
            }
        }
    }

    private void AddCostAndMaxCost(PlayerController player, int costValue)
    {
        if (player == null || costValue <= 0)
        {
            return;
        }

        player.AddMaxCost(costValue);
        player.AddCost(costValue);
    }

    private void DiscardRandomCards(PlayerController player, int discardCount)
    {
        if (player == null || player.handController == null || discardCount <= 0)
        {
            return;
        }

        List<CardController> handCards = new(player.handController.handCards);
        for (int i = 0; i < discardCount && handCards.Count > 0; i++)
        {
            int randomIndex = UnityEngine.Random.Range(0, handCards.Count);
            CardController card = handCards[randomIndex];
            handCards.RemoveAt(randomIndex);

            if (card != null)
            {
                player.SendCardToGraveyard(card);
            }
        }
    }

    private void DamageCharacters(CardController source, int damageValue, bool enemyOnly)
    {
        if (damageValue <= 0 || source == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null)
            {
                continue;
            }

            bool isEnemy = player != source.player;
            if (enemyOnly && !isEnemy)
            {
                continue;
            }

            player.Damage(damageValue);
        }

        List<CardController> targets = new();
        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player.fieldController == null)
            {
                continue;
            }

            foreach (CardController card in player.fieldController.fieldCards)
            {
                if (card == null)
                {
                    continue;
                }

                if (!enemyOnly && card == source)
                {
                    continue;
                }

                if (enemyOnly && card.player == source.player)
                {
                    continue;
                }

                targets.Add(card);
            }
        }

        foreach (CardController target in targets)
        {
            target.Damage(damageValue);
        }
    }

    private void DamageSelectedTargets(List<UnityEngine.Object> selectedTargets, int damageValue)
    {
        if (damageValue <= 0 || selectedTargets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in selectedTargets)
        {
            if (target is CardController targetCard)
            {
                targetCard.Damage(damageValue);
                continue;
            }

            if (target is PlayerController targetPlayer)
            {
                targetPlayer.Damage(damageValue);
            }
        }
    }

    private void BuffSelectedTargets(List<UnityEngine.Object> selectedTargets, int attackValue, int healthValue)
    {
        if (selectedTargets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in selectedTargets)
        {
            if (target is CardController targetCard)
            {
                targetCard.AddStats(attackValue, healthValue);
            }
        }
    }

    private void HealSelectedTargets(List<UnityEngine.Object> selectedTargets, int healValue)
    {
        if (healValue <= 0 || selectedTargets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in selectedTargets)
        {
            if (target is CardController targetCard)
            {
                targetCard.Heal(healValue);
            }
        }
    }

    private void SilenceSelectedTargets(List<UnityEngine.Object> selectedTargets)
    {
        if (selectedTargets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in selectedTargets)
        {
            if (target is not CardController targetCard)
            {
                continue;
            }

            targetCard.isSilence = true;
            if (targetCard.cardDisplay != null)
            {
                targetCard.cardDisplay.UpdateCard();
            }
        }
    }

    private void DestroySelectedTargets(List<UnityEngine.Object> selectedTargets)
    {
        if (selectedTargets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in selectedTargets)
        {
            if (target is not CardController targetCard
                || targetCard.player == null
                || targetCard.state == CardState.Graveyard)
            {
                continue;
            }

            targetCard.Kill();
        }
    }

    private void ReturnSelectedTargetsToOwnerHand(List<UnityEngine.Object> selectedTargets)
    {
        if (selectedTargets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in selectedTargets)
        {
            if (target is not CardController targetCard
                || targetCard.player == null
                || targetCard.player.handController == null)
            {
                continue;
            }

            if (targetCard.player.graveCards.Contains(targetCard))
            {
                targetCard.player.graveCards.Remove(targetCard);
                targetCard.player.RefreshGraveyardSorting();
            }

            if (targetCard.player.fieldController != null && targetCard.player.fieldController.fieldCards.Contains(targetCard))
            {
                targetCard.player.fieldController.RemoveCard(targetCard);
            }

            PlayerController owner = targetCard.player;
            CardData data = targetCard.cardData;
            targetCard.transform.localScale = Vector3.one;
            targetCard.Init(data, owner);
            if (targetCard.cardDisplay != null)
            {
                targetCard.cardDisplay.ShowBack(!owner.isMainPlayer);
            }
            owner.handController.AddCard(targetCard);
        }
    }

    private void ReviveSelectedAllies(PlayerController owner, List<UnityEngine.Object> selectedTargets)
    {
        if (owner == null || owner.fieldController == null || selectedTargets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in selectedTargets)
        {
            if (owner.fieldController.fieldCards.Count >= GameConst.fieldMax)
            {
                return;
            }

            if (target is not CardController targetCard
                || targetCard.player != owner
                || targetCard.cardData == null
                || targetCard.cardData.cardType != CardType.Minion
                || !owner.graveCards.Contains(targetCard))
            {
                continue;
            }

            owner.graveCards.Remove(targetCard);
            owner.RefreshGraveyardSorting();

            CardData data = targetCard.cardData;
            targetCard.transform.localScale = Vector3.one;
            targetCard.Init(data, owner);
            owner.fieldController.AddCard(targetCard);
        }
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
            case ConditionType.HasDiedMumber:
                return source.player.graveCards.Count > 0;
            case ConditionType.HasEmptyField:
                return source.player.fieldController != null
                    && source.player.fieldController.fieldCards.Count < GameConst.fieldMax;
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
        List<UnityEngine.Object> candidates,
        int requiredCount,
        TargetSelectionZone zone,
        List<UnityEngine.Object> selectedTargets,
        Action<List<UnityEngine.Object>> onResolved)
    {
        if (selectedTargets != null && selectedTargets.Count > 0)
        {
            onResolved?.Invoke(selectedTargets);
            return;
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
        TargetSelectionRequest request = new()
        {
            sourceCard = source,
            candidates = candidates,
            requiredCount = resolvedRequiredCount,
            zone = zone,
        };

        bool shouldAutoResolve =
            source == null
            || source.player == null
            || !source.player.isMainPlayer
            || GM.Ins == null
            || GM.Ins.BM == null
            || GM.Ins.BM.TM == null;

        if (shouldAutoResolve)
        {
            onResolved?.Invoke(ChooseRandomTargets(request));
            return;
        }

        bool selectionStarted = GM.Ins.BM.TM.SelectTargets(
            source,
            candidates,
            request.requiredCount,
            zone,
            onResolved,
            () => onResolved?.Invoke(new List<UnityEngine.Object>()));

        if (!selectionStarted)
        {
            onResolved?.Invoke(new List<UnityEngine.Object>());
        }
    }

    private static List<UnityEngine.Object> ChooseRandomTargets(TargetSelectionRequest request)
    {
        List<UnityEngine.Object> results = new();
        if (request == null || request.candidates == null || request.candidates.Count == 0)
        {
            return results;
        }

        List<UnityEngine.Object> candidates = new(request.candidates);
        int targetCount = Mathf.Min(Mathf.Max(1, request.requiredCount), candidates.Count);
        for (int i = 0; i < targetCount; i++)
        {
            int randomIndex = UnityEngine.Random.Range(0, candidates.Count);
            results.Add(candidates[randomIndex]);
            candidates.RemoveAt(randomIndex);
        }

        return results;
    }

    private static List<UnityEngine.Object> GetEnemyCharacterTargets(PlayerController sourcePlayer)
    {
        List<UnityEngine.Object> targets = GetEnemyFieldTargets(sourcePlayer);
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            return targets;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player != null && player != sourcePlayer)
            {
                targets.Add(player);
            }
        }

        return targets;
    }

    private static List<UnityEngine.Object> GetEnemyFieldTargets(PlayerController sourcePlayer)
    {
        List<UnityEngine.Object> targets = new();
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            return targets;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player == sourcePlayer || player.fieldController == null)
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

    private static List<UnityEngine.Object> GetAllyFieldTargets(PlayerController sourcePlayer)
    {
        List<UnityEngine.Object> targets = new();
        if (sourcePlayer == null || sourcePlayer.fieldController == null)
        {
            return targets;
        }

        foreach (CardController card in sourcePlayer.fieldController.fieldCards)
        {
            if (card != null)
            {
                targets.Add(card);
            }
        }

        return targets;
    }

    private static List<UnityEngine.Object> GetAllyGraveyardMinionTargets(PlayerController sourcePlayer)
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

    private static List<UnityEngine.Object> GetAllFieldTargets()
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

    private static List<UnityEngine.Object> GetOtherFieldTargets(CardController source)
    {
        List<UnityEngine.Object> targets = GetAllFieldTargets();
        if (source != null)
        {
            targets.Remove(source);
        }

        return targets;
    }
    private static int GetSelectionCount(CardEffectData effect)
    {
        if (effect == null || effect.effectValues == null)
        {
            return 1;
        }

        int index = effect.effectType == EffectType.BuffEnemy || effect.effectType == EffectType.BuffAlly ? 2 : 1;
        if (index < 0 || index >= effect.effectValues.Length || effect.effectValues[index] <= 0)
        {
            return 1;
        }

        return effect.effectValues[index];
    }

    private static int GetReviveSelectionCount(CardController source, CardEffectData effect)
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

        return Mathf.Min(GetSelectionCount(effect), openSlots);
    }

    private static int GetEffectValue(CardEffectData effect, int index)
    {
        if (effect.effectValues == null || index < 0 || index >= effect.effectValues.Length)
        {
            return 0;
        }

        return effect.effectValues[index];
    }
}
