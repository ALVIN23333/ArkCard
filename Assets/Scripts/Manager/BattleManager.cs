using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class BattleManager : MonoBehaviour
{
    public GameObject cardPrefab;
    public GameObject playQueuePoint;
    public float queuedCardScale = 0.7f;
    public float queuedCardVerticalSpacing = 1.1f;
    public int queuedCardSortingBase = 200;
    public List<PlayerController> players = new List<PlayerController>();

    private PlayerController curplayer;
    private int currentPlayerIndex;
    private bool isGameOver;
    private bool isTurnTransitioning;
    private bool isProcessingQueuedPlay;
    private readonly Dictionary<PlayerController, Transform> playerIconTargets = new();
    private readonly Queue<QueuedPlayRequest> queuedPlays = new();
    private readonly HashSet<int> projectedPlayableHandCards = new();
    private readonly BattleStateSimulator queueStateSimulator = new();
    private QueuedPlayRequest activeQueuedPlay;
    private BattleStateSnapshot projectedQueueState;
    private PlayerController projectedQueueOwner;
    private bool isQueueProjectionBlocked;
    private int projectedQueueFrame = -1;
    private float lastQueuedCardScale = float.NaN;
    private float lastQueuedCardVerticalSpacing = float.NaN;
    private int lastQueuedCardSortingBase = int.MinValue;

    public PlayerController CurrentPlayer => curplayer;
    public bool IsGameOver => isGameOver;
    public bool IsTurnTransitioning => isTurnTransitioning;
    public TargetManager TM;
    public EffectManager EM;

    private sealed class QueuedPlayRequest
    {
        public CardController card;
        public FieldController targetField;
        public List<UnityEngine.Object> selectedTargets;
        public PlayerController owner;
        public HandController originalHand;
        public int originalHandIndex;
        public Transform originalParent;
        public Vector3 originalLocalPosition;
        public Quaternion originalLocalRotation;
        public Vector3 originalLocalScale;
        public CardState originalState;
        public bool originalShowBack;
        public bool originalHadSortingGroup;
        public int originalSortingOrder;
        public bool costPaid;
        public int costBeforePayment;
        public int maxCostBeforePayment;
    }

    public void Init()
    {
        EM.Init();
        TM.Init();
        currentPlayerIndex = 0;
        isGameOver = false;
        isTurnTransitioning = true;
        isProcessingQueuedPlay = false;
        queuedPlays.Clear();
        activeQueuedPlay = null;
        projectedQueueState = null;
        projectedQueueOwner = null;
        isQueueProjectionBlocked = false;
        projectedPlayableHandCards.Clear();
        projectedQueueFrame = -1;
        playerIconTargets.Clear();

        foreach (PlayerController player in players)
        {
            player.Init();
            CachePlayerIconTarget(player);
            if (player.isMainPlayer)
            {
                curplayer = player;
                currentPlayerIndex = players.IndexOf(player);
            }
        }

        if (curplayer == null && players.Count > 0)
        {
            curplayer = players[0];
            currentPlayerIndex = 0;
        }

        AnimeManager.Delay(0.1f, () =>
        {
            foreach (PlayerController player in players)
            {
                DrawCard(player, 5);
            }
        });

        AnimeManager.Delay(1f, StartCurrentTurn);
    }


    private void CachePlayerIconTarget(PlayerController player)
    {
        if (player == null)
        {
            return;
        }

        Transform playerIcon = FindChildByName(player.transform, "PlayerIcon");
        if (playerIcon != null)
        {
            playerIconTargets[player] = playerIcon;
        }
    }

    public bool TryGetPlayerIconTarget(PlayerController player, out Transform iconTarget)
    {
        iconTarget = null;
        if (player == null)
        {
            return false;
        }

        if (playerIconTargets.TryGetValue(player, out Transform cachedTarget) && cachedTarget != null)
        {
            iconTarget = cachedTarget;
            return true;
        }

        CachePlayerIconTarget(player);
        if (playerIconTargets.TryGetValue(player, out cachedTarget) && cachedTarget != null)
        {
            iconTarget = cachedTarget;
            return true;
        }

        return false;
    }

    private Vector3 GetPlayerTargetPosition(PlayerController player)
    {
        if (TryGetPlayerIconTarget(player, out Transform playerIcon))
        {
            return playerIcon.position;
        }

        return player != null ? player.transform.position : Vector3.zero;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child;
            }
        }

        return null;
    }
    public void DrawCard(PlayerController player, int count, Action onComplete = null)
    {
        if (player == null || count <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(DrawCardsCoroutine(player, count, onComplete));
    }

    public void EndCurrentTurn()
    {
        if (players.Count == 0
            || curplayer == null
            || !curplayer.isInTurn
            || isGameOver
            || isTurnTransitioning
            || IsEffectProcessing())
        {
            return;
        }

        StartCoroutine(EndCurrentTurnCoroutine());
    }

    public void EndMainPlayerTurn()
    {
        if (!IsMainPlayerInTurn())
        {
            return;
        }

        EndCurrentTurn();
    }

    public bool IsMainPlayerInTurn()
    {
        return curplayer != null && curplayer.isMainPlayer && curplayer.isInTurn && !isGameOver && !isTurnTransitioning;
    }

    public bool CanResolveMinionAttack(CardController attacker, CardController target)
    {
        return CanAttackTarget(attacker, target);
    }

    public bool CanResolvePlayerAttack(CardController attacker, PlayerController targetPlayer)
    {
        return CanAttackTarget(attacker, targetPlayer);
    }

    public void ResolveMinionAttack(CardController attacker, CardController target)
    {
        if (!CanResolveMinionAttack(attacker, target))
        {
            return;
        }

        int attackerDamage = attacker.atk;
        int targetDamage = target.atk;
        attacker.UseAttack();

        // 横扫：在造成伤害前先记录目标相邻随从，避免目标死亡结算被移出场上列表后拿不到邻居
        List<CardController> swingleNeighbors = attacker.HasPassive(PassiveType.Swingle)
            && target != null
            && target.player != null
            && target.player.fieldController != null
            ? target.player.fieldController.GetAdjacentCards(target)
            : new List<CardController>();

        AnimeManager.PlayAttackAnimation(attacker, target.transform.position, () =>
        {
            if (target != null)
            {
                CardController.ApplyDamage(attacker, target, attackerDamage);
            }

            if (attacker != null)
            {
                CardController.ApplyDamage(target, attacker, targetDamage);
            }

            foreach (CardController neighbor in swingleNeighbors)
            {
                if (neighbor != null && neighbor != attacker)
                {
                    CardController.ApplyDamage(attacker, neighbor, attackerDamage);
                }
            }
        });
    }

    public void ResolvePlayerAttack(CardController attacker, PlayerController targetPlayer)
    {
        if (!CanResolvePlayerAttack(attacker, targetPlayer))
        {
            return;
        }

        attacker.UseAttack();
        AnimeManager.PlayAttackAnimation(attacker, GetPlayerTargetPosition(targetPlayer), () =>
        {
            if (targetPlayer != null)
            {
                CardController.ApplyPlayerDamage(attacker, targetPlayer, attacker.atk);
                CheckGameOver();
            }
        });
    }

    public bool CanUseMinion(CardController card, FieldController targetField)
    {
        if (!CanAct())
        {
            return false;
        }

        if (!CanUseCard(card) || card.cardData.cardType != CardType.Minion || targetField == null || targetField.player != card.player)
        {
            return false;
        }

        if (targetField.fieldCards.Count >= GameConst.fieldMax)
        {
            return false;
        }

        return HasRequiredConditions(card, TriggerType.Enter, false);
    }

    public bool CanUseSpell(CardController card)
    {
        if (!CanAct())
        {
            return false;
        }

        if (!CanUseCard(card)
            || card.cardData.cardType != CardType.SPELL
            || card.cardData.effects == null
            || card.cardData.effects.Count == 0)
        {
            return false;
        }

        foreach (CardEffectData effect in card.cardData.effects)
        {
            if (effect == null)
            {
                continue;
            }

            if (!EM.HasRequiredCondition(card, effect))
            {
                return false;
            }
        }

        return true;
    }

    // UI-only readiness check. Transient effect/selection locks do not hide the
    // hand action indicator because plays may still be queued.
    public bool CanShowHandCardAction(CardController card)
    {
        EnsureQueueProjection();
        return card != null
            && card.state == CardState.Hand
            && (TM == null || !TM.HasActiveSelection)
            && !isQueueProjectionBlocked
            && projectedPlayableHandCards.Contains(card.GetInstanceID());
    }

    public int QueuedPlayCount => queuedPlays.Count;
    public bool IsQueueProjectionBlocked => isQueueProjectionBlocked;

    public bool IsCardInPlayQueue(CardController card)
    {
        if (card == null)
        {
            return false;
        }

        foreach (QueuedPlayRequest request in queuedPlays)
        {
            if (request != null && request.card == card)
            {
                return true;
            }
        }

        return false;
    }

    public float GetQueuedCardDisplayScale()
    {
        return Mathf.Max(0.01f, queuedCardScale);
    }

    public bool TryQueueHandCardPlay(
        CardController card,
        FieldController targetField,
        List<UnityEngine.Object> selectedTargets = null)
    {
        if (playQueuePoint == null
            || card == null
            || card.player == null
            || card.player.handController == null
            || !card.player.handController.handCards.Contains(card))
        {
            return false;
        }

        if (TM != null && TM.HasActiveSelection)
        {
            return false;
        }

        if (activeQueuedPlay == null && queuedPlays.Count == 0)
        {
            RebuildQueueProjection();
        }
        else
        {
            EnsureQueueProjection();
        }

        if (!CanQueueHandCardPlay(card, targetField))
        {
            return false;
        }

        PlayerController owner = card.player;
        SortingGroup sortingGroup = card.GetComponent<SortingGroup>();
        QueuedPlayRequest request = new()
        {
            card = card,
            targetField = targetField,
            selectedTargets = selectedTargets != null ? new List<UnityEngine.Object>(selectedTargets) : null,
            owner = owner,
            originalHand = owner.handController,
            originalHandIndex = owner.handController.handCards.IndexOf(card),
            originalParent = card.transform.parent,
            originalLocalPosition = card.transform.localPosition,
            originalLocalRotation = card.transform.localRotation,
            originalLocalScale = card.transform.localScale,
            originalState = card.state,
            originalShowBack = card.cardDisplay != null && card.cardDisplay.back != null && card.cardDisplay.back.activeSelf,
            originalHadSortingGroup = sortingGroup != null,
            originalSortingOrder = sortingGroup != null ? sortingGroup.sortingOrder : 0,
        };

        AnimeManager.Stop(card.transform);
        owner.handController.RemoveCard(card);
        card.transform.SetParent(playQueuePoint.transform, false);
        card.state = CardState.Hanging;
        card.transform.localPosition = Vector3.zero;
        card.transform.localRotation = Quaternion.identity;
        card.transform.localScale = Vector3.one * Mathf.Max(0.01f, queuedCardScale);
        if (card.cardDisplay != null)
        {
            card.cardDisplay.ShowBack(false);
            card.cardDisplay.UpdateCard();
        }

        queuedPlays.Enqueue(request);
        RefreshPlayQueueLayout();
        AppendQueuedPlayProjection(request);
        RefreshHandActionIndicators();
        StartQueuedPlayProcessing();
        return true;
    }

    private bool CanQueueHandCardPlay(CardController card, FieldController targetField)
    {
        if (!CanAct()
            || isQueueProjectionBlocked
            || card == null
            || card.player == null
            || card.cardData == null
            || card.state != CardState.Hand
            || !card.player.isInTurn
            || !projectedPlayableHandCards.Contains(card.GetInstanceID()))
        {
            return false;
        }

        if (card.cardData.cardType == CardType.Minion)
        {
            if (targetField == null || targetField.player != card.player)
            {
                return false;
            }

            return true;
        }

        if (card.cardData.cardType != CardType.SPELL
            || card.cardData.effects == null
            || card.cardData.effects.Count == 0)
        {
            return false;
        }

        return true;
    }

    public bool CanUseFieldCast(CardController card)
    {
        if (!CanAct())
        {
            return false;
        }

        if (!CanUseFieldCard(card)
            || card.cardData.cardType != CardType.Minion
            || card.isSilence
            || card.castUsed
            || card.cardData.effects == null
            || card.cardData.effects.Count == 0)
        {
            return false;
        }

        bool hasCastEffect = false;
        foreach (CardEffectData effect in card.cardData.effects)
        {
            if (effect == null || effect.triggerType != TriggerType.Cast)
            {
                continue;
            }

            hasCastEffect = true;
            if (!EM.HasRequiredCondition(card, effect))
            {
                return false;
            }
        }

        if (!hasCastEffect)
        {
            return false;
        }

        return true;
    }

    public bool TryUseFieldCast(CardController card, bool autoSelect = false, List<UnityEngine.Object> selectedTargets = null)
    {
        if (!CanUseFieldCast(card))
        {
            return false;
        }

        ResolveFieldCast(card, selectedTargets);
        return true;
    }

    public void ResetFieldAttackState(PlayerController player)
    {
        if (player == null || player.fieldController == null)
        {
            return;
        }

        foreach (CardController card in player.fieldController.fieldCards)
        {
            if (card == null || card.state != CardState.Field)
            {
                continue;
            }

            card.RefreshTurnAttackState();
        }
    }


    private bool HasRequiredConditions(CardController card, TriggerType triggerType, bool requireMatchingEffect)
    {
        if (card == null || card.cardData == null || card.cardData.effects == null)
        {
            return !requireMatchingEffect;
        }

        bool hasMatchingEffect = false;
        foreach (CardEffectData effect in card.cardData.effects)
        {
            if (effect == null || effect.triggerType != triggerType)
            {
                continue;
            }

            hasMatchingEffect = true;
            if (!EM.HasRequiredCondition(card, effect))
            {
                return false;
            }
        }

        return !requireMatchingEffect || hasMatchingEffect;
    }
    private IEnumerator DrawCardsCoroutine(PlayerController player, int count, Action onComplete)
    {
        for (int i = 0; i < count; i++)
        {
            if (count > 1)
            {
                yield return new WaitForSeconds(0.1f);
            }

            // Overdraw cards use TargetManager's single pending slot. Wait for
            // the current card to finish before starting another hanging sequence.
            while (TM != null && TM.HasPendingCard && !isGameOver)
            {
                yield return null;
            }

            if (isGameOver)
            {
                onComplete?.Invoke();
                yield break;
            }

            bool drawComplete = false;
            player.DrawCard(() => drawComplete = true);
            while (!drawComplete && !isGameOver)
            {
                yield return null;
            }

            while (TM != null && TM.HasPendingCard && !isGameOver)
            {
                yield return null;
            }
        }

        onComplete?.Invoke();
    }

    private void EnsureQueueProjection()
    {
        if (activeQueuedPlay != null || queuedPlays.Count > 0)
        {
            return;
        }

        if (projectedQueueFrame == Time.frameCount && projectedQueueOwner == curplayer)
        {
            return;
        }

        RebuildQueueProjection();
    }

    private void RebuildQueueProjection()
    {
        projectedPlayableHandCards.Clear();
        projectedQueueState = null;
        projectedQueueOwner = curplayer;
        isQueueProjectionBlocked = false;
        projectedQueueFrame = Time.frameCount;

        if (projectedQueueOwner == null || players == null)
        {
            return;
        }

        int ownerIndex = players.IndexOf(projectedQueueOwner);
        if (!SnapshotFactory.TryCreate(this, ownerIndex, out projectedQueueState, out string error))
        {
            projectedQueueState = null;
            isQueueProjectionBlocked = true;
            Debug.LogWarning($"[BattleManager] Queue projection unavailable: {error}");
            return;
        }

        foreach (QueuedPlayRequest request in queuedPlays)
        {
            if (!AppendQueuedPlayProjection(request))
            {
                break;
            }
        }

        RefreshProjectedPlayableCards();
    }

    private bool AppendQueuedPlayProjection(QueuedPlayRequest request)
    {
        if (request == null || request.card == null || projectedQueueState == null || isQueueProjectionBlocked)
        {
            return false;
        }

        int ownerIndex = players.IndexOf(request.owner);
        PlayerStateSnapshot owner = projectedQueueState.GetPlayer(ownerIndex);
        if (owner == null)
        {
            isQueueProjectionBlocked = true;
            projectedPlayableHandCards.Clear();
            return false;
        }

        CardStateSnapshot source = projectedQueueState.FindCard(request.card.GetInstanceID());
        if (source == null)
        {
            source = SnapshotFactory.CreateCardSnapshot(request.card, ownerIndex, CardState.Hand);
            if (source == null)
            {
                isQueueProjectionBlocked = true;
                projectedPlayableHandCards.Clear();
                return false;
            }

            owner.Hand.Add(source);
        }

        if (HasUnpredictableQueueEffects(source.Data)
            || (request.owner != null
                && request.owner.isMainPlayer
                && HasUnresolvedManualSelection(source, request.selectedTargets)))
        {
            isQueueProjectionBlocked = true;
            projectedPlayableHandCards.Clear();
            return false;
        }

        SimulatedAction action = FindProjectedPlayAction(projectedQueueState, source, request.selectedTargets);
        if (action == null)
        {
            isQueueProjectionBlocked = true;
            projectedPlayableHandCards.Clear();
            return false;
        }

        projectedQueueState = queueStateSimulator.ApplyAction(
            projectedQueueState,
            action,
            new System.Random(7919 + request.card.GetInstanceID()));
        RefreshProjectedPlayableCards();
        return true;
    }

    private void RefreshProjectedPlayableCards()
    {
        projectedPlayableHandCards.Clear();
        if (projectedQueueState == null || isQueueProjectionBlocked)
        {
            return;
        }

        PlayerStateSnapshot owner = projectedQueueState.GetPlayer(projectedQueueState.CurrentPlayerIndex);
        if (owner == null)
        {
            return;
        }

        foreach (SimulatedAction action in queueStateSimulator.GenerateLegalActions(projectedQueueState))
        {
            if (action != null && action.Type == SimulatedActionType.PlayHandCard)
            {
                projectedPlayableHandCards.Add(action.SourceCardId);
            }
        }
    }

    private SimulatedAction FindProjectedPlayAction(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        List<UnityEngine.Object> selectedTargets)
    {
        List<SimulatedTarget> mappedTargets = MapSimulationTargets(selectedTargets);
        foreach (SimulatedAction action in queueStateSimulator.GenerateLegalActions(state))
        {
            if (action == null
                || action.Type != SimulatedActionType.PlayHandCard
                || action.SourceCardId != source.RuntimeId)
            {
                continue;
            }

            if (TargetsMatch(action.Targets, mappedTargets))
            {
                return action;
            }
        }

        return null;
    }

    private List<SimulatedTarget> MapSimulationTargets(List<UnityEngine.Object> selectedTargets)
    {
        List<SimulatedTarget> result = new();
        if (selectedTargets == null)
        {
            return result;
        }

        foreach (UnityEngine.Object target in selectedTargets)
        {
            if (target is CardController card)
            {
                result.Add(SimulatedTarget.Card(card.GetInstanceID()));
            }
            else if (target is PlayerController player)
            {
                int index = players.IndexOf(player);
                if (index >= 0)
                {
                    result.Add(SimulatedTarget.Player(index));
                }
            }
        }

        return result;
    }

    private static bool TargetsMatch(List<SimulatedTarget> expected, List<SimulatedTarget> actual)
    {
        if (expected == null || actual == null || expected.Count != actual.Count)
        {
            return false;
        }

        foreach (SimulatedTarget target in expected)
        {
            if (target == null || !actual.Contains(target))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasUnresolvedManualSelection(CardStateSnapshot source, List<UnityEngine.Object> selectedTargets)
    {
        if (source == null || source.Data == null || source.Data.effects == null)
        {
            return false;
        }

        List<SimulatedTarget> mappedTargets = MapSimulationTargets(selectedTargets);
        foreach (CardEffectData effect in source.Data.effects)
        {
            if (ShouldResolveOnHandPlay(source.Data, effect)
                && RequiresManualSelection(effect)
                && mappedTargets.Count == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresManualSelection(CardEffectData effect)
    {
        if (effect == null)
        {
            return false;
        }

        ICardEffectDefinition definition = EffectRegistry.Get(effect.effectType);
        if (definition.RequiresTargetSelection(effect))
        {
            return true;
        }

        if (effect.thenEffects != null)
        {
            foreach (CardEffectData nested in effect.thenEffects)
            {
                if (RequiresManualSelection(nested)) return true;
            }
        }

        if (effect.elseEffects != null)
        {
            foreach (CardEffectData nested in effect.elseEffects)
            {
                if (RequiresManualSelection(nested)) return true;
            }
        }

        return false;
    }

    private static bool HasUnpredictableQueueEffects(CardData data)
    {
        if (data == null || data.effects == null)
        {
            return false;
        }

        foreach (CardEffectData effect in data.effects)
        {
            if (ShouldResolveOnHandPlay(data, effect)
                && HasUnpredictableQueueEffect(effect))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldResolveOnHandPlay(CardData data, CardEffectData effect)
    {
        return data != null
            && effect != null
            && (data.cardType == CardType.SPELL || effect.triggerType == TriggerType.Enter);
    }

    private static bool HasUnpredictableQueueEffect(CardEffectData effect)
    {
        if (effect == null)
        {
            return false;
        }

        if (effect.targetMode == EffectTargetMode.Random
            || effect.effectType == EffectType.DisCard
            || effect.effectType == EffectType.Discard
            || effect.effectType == EffectType.SummonRandomCostMinion)
        {
            return true;
        }

        if (effect.thenEffects != null)
        {
            foreach (CardEffectData nested in effect.thenEffects)
            {
                if (HasUnpredictableQueueEffect(nested)) return true;
            }
        }

        if (effect.elseEffects != null)
        {
            foreach (CardEffectData nested in effect.elseEffects)
            {
                if (HasUnpredictableQueueEffect(nested)) return true;
            }
        }

        return false;
    }

    private void StartQueuedPlayProcessing()
    {
        if (!isProcessingQueuedPlay)
        {
            StartCoroutine(ProcessQueuedPlaysCoroutine());
        }
    }

    private IEnumerator ProcessQueuedPlaysCoroutine()
    {
        isProcessingQueuedPlay = true;
        while (queuedPlays.Count > 0)
        {
            while (!CanProcessQueuedPlayNow())
            {
                if (isGameOver || queuedPlays.Peek().owner == null || !queuedPlays.Peek().owner.isInTurn)
                {
                    CancelAllQueuedPlays();
                    isProcessingQueuedPlay = false;
                    yield break;
                }

                yield return null;
            }

            QueuedPlayRequest request = queuedPlays.Dequeue();
            RefreshPlayQueueLayout();
            activeQueuedPlay = request;
            AnimeManager.Stop(request.card != null ? request.card.transform : null);
            if (!IsQueuedPlayStillValid(request))
            {
                CancelQueuedPlay(request);
                activeQueuedPlay = null;
                RebuildQueueProjection();
                RefreshHandActionIndicators();
                continue;
            }

            request.costBeforePayment = request.owner.cost;
            request.maxCostBeforePayment = request.owner.maxCost;
            if (!request.owner.SpendCost(request.card.cost))
            {
                CancelQueuedPlay(request);
                activeQueuedPlay = null;
                RebuildQueueProjection();
                RefreshHandActionIndicators();
                continue;
            }

            request.costPaid = true;
            RegisterQueuedPlayRollback(request);
            bool completed = false;
            ExecuteQueuedPlay(request, () => completed = true);
            while (!completed)
            {
                yield return null;
            }

            activeQueuedPlay = null;
            RebuildQueueProjection();
            RefreshHandActionIndicators();
        }

        isProcessingQueuedPlay = false;
        RefreshPlayQueueLayout();
    }

    private bool CanProcessQueuedPlayNow()
    {
        return !isGameOver
            && !isTurnTransitioning
            && (EM == null || !EM.IsProcessingEffects)
            && (TM == null || (!TM.HasPendingCard && !TM.HasActiveSelection));
    }

    private bool IsQueuedPlayStillValid(QueuedPlayRequest request)
    {
        CardController card = request != null ? request.card : null;
        if (request == null
            || card == null
            || request.owner == null
            || card.player != request.owner
            || card.cardData == null
            || card.state != CardState.Hanging)
        {
            return false;
        }

        if (card.cardData.cardType == CardType.Minion)
        {
            return request.targetField != null
                && request.targetField.player == request.owner
                && request.targetField.fieldCards.Count < GameConst.fieldMax
                && HasRequiredConditions(card, TriggerType.Enter, false);
        }

        if (card.cardData.cardType != CardType.SPELL
            || card.cardData.effects == null
            || card.cardData.effects.Count == 0)
        {
            return false;
        }

        foreach (CardEffectData effect in card.cardData.effects)
        {
            if (effect != null && EM != null && !EM.HasRequiredCondition(card, effect))
            {
                return false;
            }
        }

        return true;
    }

    private void RegisterQueuedPlayRollback(QueuedPlayRequest request)
    {
        if (TM == null || request == null || request.card == null)
        {
            return;
        }

        CardController card = request.card;
        Transform queuedParent = card.transform.parent;
        Vector3 queuedPosition = card.transform.localPosition;
        Quaternion queuedRotation = card.transform.localRotation;
        Vector3 queuedScale = card.transform.localScale;
        CardState queuedState = card.state;
        bool queuedShowBack = card.cardDisplay != null && card.cardDisplay.back != null && card.cardDisplay.back.activeSelf;
        HandController hand = request.originalHand;
        bool insertedIntoHand = false;

        if (request.originalParent != null)
        {
            card.transform.SetParent(request.originalParent, false);
        }

        card.transform.localPosition = request.originalLocalPosition;
        card.transform.localRotation = request.originalLocalRotation;
        card.transform.localScale = request.originalLocalScale;
        card.state = request.originalState;
        if (card.cardDisplay != null)
        {
            card.cardDisplay.ShowBack(request.originalShowBack);
        }

        if (hand != null && !hand.handCards.Contains(card))
        {
            int index = Mathf.Clamp(request.originalHandIndex, 0, hand.handCards.Count);
            hand.handCards.Insert(index, card);
            insertedIntoHand = true;
        }

        TM.RegisterPlayedCardRollback(
            card,
            request.owner,
            request.costBeforePayment,
            request.maxCostBeforePayment);
        if (insertedIntoHand)
        {
            hand.handCards.Remove(card);
        }

        card.transform.SetParent(queuedParent, false);
        card.transform.localPosition = queuedPosition;
        card.transform.localRotation = queuedRotation;
        card.transform.localScale = queuedScale;
        card.state = queuedState;
        if (card.cardDisplay != null)
        {
            card.cardDisplay.ShowBack(queuedShowBack);
            card.cardDisplay.UpdateCard();
        }
    }

    private void ExecuteQueuedPlay(QueuedPlayRequest request, Action onComplete)
    {
        if (request.card.cardData.cardType == CardType.SPELL)
        {
            ExecuteQueuedSpell(request, onComplete);
            return;
        }

        ExecuteQueuedMinion(request, onComplete);
    }

    private void ExecuteQueuedSpell(QueuedPlayRequest request, Action onComplete)
    {
        CardController card = request.card;
        AnimeManager.Stop(card.transform);
        TargetManager targetManager = TM;
        bool wasCancelled = false;

        void TriggerSpellAfterHangingReady()
        {
            targetManager?.ReleasePendingSlot(card);

            void CancelSpell()
            {
                wasCancelled = true;
                CancelQueuedPlay(request);
            }

            void CompleteSpell()
            {
                void Finish()
                {
                    if (!wasCancelled
                        && card != null
                        && card.player != null
                        && card.state == CardState.Hanging)
                    {
                        card.player.SendCardToGraveyard(card);
                    }

                    targetManager?.ClearPlayedCardRollback(card);
                    onComplete?.Invoke();
                }

                Finish();
            }

            if (EM != null)
            {
                EM.TriggerSpellEffect(card, request.selectedTargets, CompleteSpell, CancelSpell);
            }
            else
            {
                CompleteSpell();
            }
        }

        if (targetManager == null || !targetManager.EnterHangingState(card, false, TriggerSpellAfterHangingReady))
        {
            TriggerSpellAfterHangingReady();
        }
    }

    private void ExecuteQueuedMinion(QueuedPlayRequest request, Action onComplete)
    {
        CardController card = request.card;
        request.targetField.AddCard(card);
        card.transform.localScale = Vector3.one;
        AnimeManager.Delay(AnimeManager.FieldRefreshDuration, () =>
        {
            if (card == null || card.state != CardState.Field)
            {
                TM?.ClearPlayedCardRollback(card);
                onComplete?.Invoke();
                return;
            }

            if (EM != null)
            {
                EM.TriggerCardEffect(card, TriggerType.Enter, request.selectedTargets, () =>
                {
                    TM?.ClearPlayedCardRollback(card);
                    onComplete?.Invoke();
                });
            }
            else
            {
                TM?.ClearPlayedCardRollback(card);
                onComplete?.Invoke();
            }
        });
    }

    private void CancelAllQueuedPlays()
    {
        while (queuedPlays.Count > 0)
        {
            CancelQueuedPlay(queuedPlays.Dequeue());
        }

        RefreshPlayQueueLayout();
        RebuildQueueProjection();
        RefreshHandActionIndicators();
    }

    private void CancelQueuedPlay(QueuedPlayRequest request)
    {
        if (request == null || request.card == null || request.owner == null)
        {
            return;
        }

        CardController card = request.card;
        AnimeManager.Stop(card.transform);
        request.owner.handController?.RemoveCard(card);
        request.owner.fieldController?.RemoveCard(card);
        request.owner.graveCards.Remove(card);
        request.owner.RefreshGraveyardSorting();

        if (request.originalParent != null)
        {
            card.transform.SetParent(request.originalParent, false);
        }

        card.transform.localPosition = request.originalLocalPosition;
        card.transform.localRotation = request.originalLocalRotation;
        card.transform.localScale = request.originalLocalScale;
        card.state = request.originalState;
        SortingGroup group = card.GetComponent<SortingGroup>();
        if (group != null && request.originalHadSortingGroup)
        {
            group.sortingOrder = request.originalSortingOrder;
        }

        if (request.costPaid)
        {
            request.owner.RestoreCostState(request.costBeforePayment, request.maxCostBeforePayment);
            request.costPaid = false;
        }
        if (request.originalHand != null)
        {
            request.originalHand.InsertCard(card, request.originalHandIndex);
        }

        if (card.cardDisplay != null)
        {
            card.cardDisplay.ShowBack(request.originalShowBack);
            card.cardDisplay.UpdateCard();
        }

        RefreshPlayQueueLayout();
    }

    private void RefreshPlayQueueLayout()
    {
        if (playQueuePoint == null)
        {
            return;
        }

        lastQueuedCardScale = queuedCardScale;
        lastQueuedCardVerticalSpacing = queuedCardVerticalSpacing;
        lastQueuedCardSortingBase = queuedCardSortingBase;

        int index = 0;
        foreach (QueuedPlayRequest request in queuedPlays)
        {
            CardController card = request != null ? request.card : null;
            if (card == null)
            {
                continue;
            }

            AnimeManager.Stop(card.transform);
            card.transform.SetParent(playQueuePoint.transform, false);
            card.transform.localPosition = new Vector3(0f, -index * queuedCardVerticalSpacing, 0f);
            card.transform.localRotation = Quaternion.identity;
            card.transform.localScale = Vector3.one * Mathf.Max(0.01f, queuedCardScale);
            SortingGroup group = card.GetComponent<SortingGroup>();
            if (group != null)
            {
                group.sortingOrder = queuedCardSortingBase + index;
            }

            index++;
        }
    }

    public void RefreshHandActionIndicators()
    {
        if (curplayer == null || curplayer.handController == null)
        {
            return;
        }

        foreach (CardController handCard in curplayer.handController.handCards)
        {
            handCard?.cardDisplay?.UpdateCard();
        }
    }

    private void LateUpdate()
    {
        if (playQueuePoint != null
            && (lastQueuedCardScale != queuedCardScale
                || lastQueuedCardVerticalSpacing != queuedCardVerticalSpacing
                || lastQueuedCardSortingBase != queuedCardSortingBase))
        {
            RefreshPlayQueueLayout();
        }
    }

    private void DrawSingleCard(PlayerController player)
    {
        if (player != null)
        {
            player.DrawCard();
        }
    }

    private void StartCurrentTurn()
    {
        if (curplayer == null || isGameOver)
        {
            isTurnTransitioning = false;
            return;
        }

        StartCoroutine(StartCurrentTurnCoroutine());
    }

    private IEnumerator EndCurrentTurnCoroutine()
    {
        isTurnTransitioning = true;
        PlayerController endingPlayer = curplayer;
        endingPlayer.EndTurn();

        yield return new WaitForSeconds(0.2f);

        if (CheckGameOver())
        {
            yield break;
        }

        EM.TriggerFieldEffects(endingPlayer, TriggerType.End);
        while (IsEffectProcessing() && !isGameOver)
        {
            yield return null;
        }

        if (CheckGameOver())
        {
            yield break;
        }

        currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
        curplayer = players[currentPlayerIndex];
        StartCurrentTurn();
    }

    private IEnumerator StartCurrentTurnCoroutine()
    {
        isTurnTransitioning = true;
        ResetFieldAttackState(curplayer);
        curplayer.StartTurn();

        bool turnPanelComplete = false;
        if (GM.Ins != null && GM.Ins.UM != null)
        {
            GM.Ins.UM.ShowTurnPanel(curplayer, () => turnPanelComplete = true);
        }
        else
        {
            AnimeManager.Delay(0.2f, () => turnPanelComplete = true);
        }

        while (!turnPanelComplete && !isGameOver)
        {
            yield return null;
        }

        if (CheckGameOver())
        {
            yield break;
        }

        EM.TriggerFieldEffects(curplayer, TriggerType.Start);
        while (IsEffectProcessing() && !isGameOver)
        {
            yield return null;
        }

        if (CheckGameOver())
        {
            yield break;
        }

        DrawCard(curplayer, GameConst.turnDraw);
        isTurnTransitioning = false;
    }

    public bool CheckGameOver()
    {
        if (isGameOver)
        {
            return true;
        }

        if (IsEffectProcessing())
        {
            return false;
        }

        bool mainDead = false;
        bool enemyDead = false;
        foreach (PlayerController player in players)
        {
            if (player == null || player.health > 0)
            {
                continue;
            }

            if (player.isMainPlayer)
            {
                mainDead = true;
            }
            else
            {
                enemyDead = true;
            }
        }

        if (!mainDead && !enemyDead)
        {
            return false;
        }

        if (mainDead && enemyDead)
        {
            EndGame(null, true);
            return true;
        }

        PlayerController winner = FindAlivePlayer(!mainDead);
        EndGame(winner, winner == null);
        return true;
    }

    private void EndGame(PlayerController winner, bool isDraw)
    {
        isGameOver = true;
        isTurnTransitioning = false;

        foreach (PlayerController player in players)
        {
            if (player != null && player.isInTurn)
            {
                player.EndTurn();
            }
        }

        if (GM.Ins != null && GM.Ins.UM != null)
        {
            GM.Ins.UM.ShowWinPanel(winner, isDraw);
        }
    }

    private PlayerController FindAlivePlayer(bool isMainPlayer)
    {
        foreach (PlayerController player in players)
        {
            if (player != null && player.isMainPlayer == isMainPlayer && player.health > 0)
            {
                return player;
            }
        }

        return null;
    }

    private void ResolveFieldCast(CardController card, List<UnityEngine.Object> selectedTargets)
    {
        if (!CanUseFieldCast(card))
        {
            return;
        }

        TM?.RegisterFieldCastRollback(card);
        card.SetCastUsed(true);
        EM.TriggerCardEffect(card, TriggerType.Cast, selectedTargets, () =>
        {
            TM?.ClearFieldCastRollback(card);
        });
    }
    private bool CanAttack(CardController attacker)
    {
        return CanAct()
            && attacker != null
            && attacker.state == CardState.Field
            && attacker.canAttack
            && attacker.attackCount > 0
            && attacker.player != null
            && attacker.player.isInTurn;
    }

    private bool CanAttackTarget(CardController attacker, CardController target)
    {
        if (!CanAttack(attacker)
            || target == null
            || target.state != CardState.Field
            || target.player == null
            || target.player == attacker.player)
        {
            return false;
        }

        if (target.isStealth)
        {
            return false;
        }

        return !HasGuardMinion(target.player) || IsGuardMinion(target);
    }

    private bool CanAttackTarget(CardController attacker, PlayerController targetPlayer)
    {
        if (!CanAttack(attacker) || !attacker.canAttackPlayer || targetPlayer == null || targetPlayer == attacker.player)
        {
            return false;
        }

        return !HasGuardMinion(targetPlayer);
    }

    private static bool HasGuardMinion(PlayerController player)
    {
        if (player == null || player.fieldController == null)
        {
            return false;
        }

        foreach (CardController card in player.fieldController.fieldCards)
        {
            if (IsGuardMinion(card))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGuardMinion(CardController card)
    {
        return card != null
            && card.state == CardState.Field
            && !card.isSilence
            && !card.isStealth
            && card.cardData != null
            && card.HasPassive(PassiveType.Guard);
    }

    private bool CanUseCard(CardController card)
    {
        return CanAct()
            && !IsEffectProcessing()
            && card != null
            && card.player != null
            && card.cardData != null
            && card.state == CardState.Hand
            && card.player.isInTurn
            && card.player.cost >= card.cost;
    }

    private bool CanUseFieldCard(CardController card)
    {
        return CanAct()
            && !IsEffectProcessing()
            && card != null
            && card.player != null
            && card.cardData != null
            && card.state == CardState.Field
            && card.player.isInTurn;
    }

    private bool CanAct()
    {
        return !isGameOver && !isTurnTransitioning;
    }

    private bool IsEffectProcessing()
    {
        return isProcessingQueuedPlay
            || queuedPlays.Count > 0
            || (EM != null && EM.IsProcessingEffects);
    }
}
