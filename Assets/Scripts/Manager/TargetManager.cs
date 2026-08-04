using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public enum TargetSelectionZone
{
    Field,
    Graveyard,
}

[Serializable]
public class TargetSelectionRequest
{
    public CardController sourceCard;
    public List<UnityEngine.Object> candidates = new();
    public int requiredCount = 1;
    public TargetSelectionZone zone;
    public Action<List<UnityEngine.Object>> onComplete;
    public Action onCancel;
}

public class TargetManager : MonoBehaviour
{
    public GameObject effectPoint;
    [SerializeField]
    public GameObject selectObj;
    [SerializeField]
    public GameObject selectPanel;
    [SerializeField]
    private Transform graveSelectionCameraTarget;
    [SerializeField]
    private Vector3 hangingLocalPosition = new Vector3(9f, 0f, 0f);
    [SerializeField]
    private Vector3 hangingScale = Vector3.one * 1.5f;
    [SerializeField]
    private float selectionSpacing = 3.5f;
    [SerializeField]
    private Vector3 panelStartLocalPosition = Vector3.zero;
    [SerializeField]
    private float panelHorizontalSpacing = 3.5f;
    [SerializeField]
    private float panelVerticalSpacing = 2.5f;
    [SerializeField]
    private int panelMaxPerRow = 4;

    private const float MinimumHangingDuration = 0.1f;
    private const int HangingSortingOrderOffset = 100;
    private const float SelectionMarkerBaseScale = 2f;
    private const float SelectionMarkerPulseScale = 2.5f;
    private const float SelectionMarkerPulseDuration = 0.12f;
    private const float SelectionMarkerRotationSpeed = 180f;

    private readonly List<UnityEngine.Object> availableTargets = new();
    private readonly List<UnityEngine.Object> selectedTargets = new();
    private readonly List<SelectionMarkerState> selectionMarkers = new();
    private readonly Dictionary<CardController, GraveCardState> graveCardStates = new();
    private readonly List<RollbackStep> rollbackSteps = new();

    private TargetSelectionRequest currentRequest;
    private CardController pendingCard;
    private HandController pendingOriginalHand;
    private FieldController pendingOriginalField;
    private Transform pendingOriginalParent;
    private Vector3 pendingOriginalLocalPosition;
    private Quaternion pendingOriginalLocalRotation;
    private Vector3 pendingOriginalLocalScale;
    private CardState pendingOriginalState;
    private bool pendingOriginalShowBack;
    private bool pendingOriginalHadSortingGroup;
    private int pendingOriginalSortingOrder;
    private int pendingOriginalHandIndex = -1;
    private int pendingOriginalFieldIndex = -1;
    private bool pendingRestoreOnCancel = true;
    private float pendingHangingReleaseTime;
    private int requiredCount = 1;
    private bool cameraStateCached;
    private Vector3 originalCameraPosition;
    private Quaternion originalCameraRotation;
    private bool panelSelectionActive;

    public bool HasActiveSelection => currentRequest != null;
    public bool HasPendingCard => pendingCard != null;
    public CardController PendingCard => pendingCard;

    private sealed class GraveCardState
    {
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public bool showBack;
        public int sortingOrder;
    }

    private sealed class SelectionMarkerState
    {
        public UnityEngine.Object target;
        public GameObject instance;
    }

    private enum RollbackStepType
    {
        SelectedTarget,
        PlayedCard,
        FieldCast,
    }

    private sealed class RollbackStep
    {
        public RollbackStepType type;
        public UnityEngine.Object selectedTarget;
        public PlayedCardState playedCardState;
        public FieldCastState fieldCastState;
    }

    private sealed class FieldCastState
    {
        public CardController card;
        public bool castUsed;
    }

    private sealed class PlayedCardState
    {
        public CardController card;
        public PlayerController player;
        public HandController hand;
        public FieldController field;
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public CardState state;
        public bool showBack;
        public bool hadSortingGroup;
        public int sortingOrder;
        public int handIndex;
        public int fieldIndex;
        public int costBefore;
        public int maxCostBefore;
        public int cardCost;
        public int attack;
        public int health;
        public int maxHealth;
        public bool canAttack;
        public bool castUsed;
        public bool isSilence;
        public bool isDying;
    }

    private void Update()
    {
        if (HasActiveSelection)
        {
            UpdateSelectionObjectPosition();
        }

        UpdateSelectionMarkers();
    }
    public void Init()
    {
        EnsureSelectionObjects();
        EnsureEffectPoint();

        if (selectPanel != null && selectObj != null)
        {
            selectObj.transform.position = selectPanel.transform.position;
        }

        if (graveSelectionCameraTarget == null)
        {
            GameObject cameraTargetObject = new GameObject("GraveSelectionCameraTarget");
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector3 cameraPosition = mainCamera.transform.position;
                if (selectPanel != null)
                {
                    cameraPosition.x = selectPanel.transform.position.x;
                }
                else
                {
                    cameraPosition.x += 12f;
                }

                cameraTargetObject.transform.position = cameraPosition;
                cameraTargetObject.transform.rotation = mainCamera.transform.rotation;
            }
            else if (selectPanel != null)
            {
                cameraTargetObject.transform.position = selectPanel.transform.position;
            }

            graveSelectionCameraTarget = cameraTargetObject.transform;
        }
    }

    public bool BeginSelection(TargetSelectionRequest request)
    {
        if (request == null || request.sourceCard == null)
        {
            return false;
        }

        if (HasActiveSelection)
        {
            CancelCurrentSelection();
        }

        List<UnityEngine.Object> candidates = BuildUniqueCandidateList(request.candidates);
        if (candidates.Count == 0)
        {
            return false;
        }

        currentRequest = request;
        availableTargets.Clear();
        availableTargets.AddRange(candidates);
        selectedTargets.Clear();
        requiredCount = Mathf.Min(Mathf.Max(1, request.requiredCount), candidates.Count);
        currentRequest.requiredCount = requiredCount;
        ShowSelectionObject();

        if (request.sourceCard.state == CardState.Hand && pendingCard != request.sourceCard)
        {
            EnterHangingState(request.sourceCard, true);
        }

        if (request.zone == TargetSelectionZone.Graveyard)
        {
            PresentGraveyardTargets();
        }

        RefreshTargetCards();
        return true;
    }

    public bool SelectTargets(
        CardController sourceCard,
        List<UnityEngine.Object> candidates,
        int requiredCount,
        TargetSelectionZone zone,
        Action<List<UnityEngine.Object>> onComplete,
        Action onCancel = null)
    {
        return BeginSelection(new TargetSelectionRequest
        {
            sourceCard = sourceCard,
            candidates = candidates,
            requiredCount = requiredCount,
            zone = zone,
            onComplete = onComplete,
            onCancel = onCancel,
        });
    }

    public void RegisterPlayedCardRollback(CardController card, PlayerController player, int costBefore, int maxCostBefore)
    {
        if (card == null || player == null)
        {
            return;
        }

        ClearPlayedCardRollback(card);

        SortingGroup group = card.GetComponent<SortingGroup>();
        HandController hand = player.handController;
        FieldController field = player.fieldController;
        int handIndex = hand != null ? hand.handCards.IndexOf(card) : -1;
        int fieldIndex = field != null ? field.fieldCards.IndexOf(card) : -1;
        Transform parent = card.transform.parent;
        Vector3 localPosition = card.transform.localPosition;
        Quaternion localRotation = card.transform.localRotation;
        Vector3 localScale = card.transform.localScale;
        CardState state = card.state;
        bool showBack = card.cardDisplay != null && card.cardDisplay.back != null && card.cardDisplay.back.activeSelf;
        int sortingOrder = group != null ? group.sortingOrder : 0;

        if (pendingCard == card)
        {
            hand = pendingOriginalHand != null ? pendingOriginalHand : hand;
            field = pendingOriginalField != null ? pendingOriginalField : field;
            handIndex = pendingOriginalHandIndex;
            fieldIndex = pendingOriginalFieldIndex;
            parent = pendingOriginalParent != null ? pendingOriginalParent : parent;
            localPosition = pendingOriginalLocalPosition;
            localRotation = pendingOriginalLocalRotation;
            localScale = pendingOriginalLocalScale;
            state = pendingOriginalState;
            showBack = pendingOriginalShowBack;
            sortingOrder = pendingOriginalSortingOrder;
        }

        rollbackSteps.Add(new RollbackStep
        {
            type = RollbackStepType.PlayedCard,
            playedCardState = new PlayedCardState
            {
                card = card,
                player = player,
                hand = hand,
                field = field,
                parent = parent,
                localPosition = localPosition,
                localRotation = localRotation,
                localScale = localScale,
                state = state,
                showBack = showBack,
                hadSortingGroup = group != null,
                sortingOrder = sortingOrder,
                handIndex = handIndex,
                fieldIndex = fieldIndex,
                costBefore = costBefore,
                maxCostBefore = maxCostBefore,
                cardCost = card.cost,
                attack = card.atk,
                health = card.health,
                maxHealth = card.maxHealth,
                canAttack = card.canAttack,
                castUsed = card.castUsed,
                isSilence = card.isSilence,
                isDying = card.isDying,
            },
        });
    }

    public void ClearPlayedCardRollback(CardController card)
    {
        rollbackSteps.RemoveAll(step =>
            step != null
            && step.type == RollbackStepType.PlayedCard
            && step.playedCardState != null
            && step.playedCardState.card == card);
    }

    public void RegisterFieldCastRollback(CardController card)
    {
        if (card == null)
        {
            return;
        }

        ClearFieldCastRollback(card);
        rollbackSteps.Add(new RollbackStep
        {
            type = RollbackStepType.FieldCast,
            fieldCastState = new FieldCastState
            {
                card = card,
                castUsed = card.castUsed,
            },
        });
    }

    public void ClearFieldCastRollback(CardController card)
    {
        rollbackSteps.RemoveAll(step =>
            step != null
            && step.type == RollbackStepType.FieldCast
            && step.fieldCastState != null
            && step.fieldCastState.card == card);
    }

    public bool TrySelectTarget(CardController target)
    {
        return TrySelectTargetInternal(target);
    }

    public bool TrySelectTarget(PlayerController target)
    {
        return TrySelectTargetInternal(target);
    }

    public bool UndoLastSelectionOrPending()
    {
        if (!HasActiveSelection)
        {
            return false;
        }

        if (rollbackSteps.Count > 0)
        {
            RollbackStep step = rollbackSteps[rollbackSteps.Count - 1];
            rollbackSteps.RemoveAt(rollbackSteps.Count - 1);
            ExecuteRollbackStep(step);
            return true;
        }

        CancelCurrentSelection();
        return true;
    }

    public void CancelCurrentSelection()
    {
        if (!HasActiveSelection)
        {
            return;
        }

        TargetSelectionRequest request = currentRequest;
        RestoreTransientSelectionState();
        if (pendingRestoreOnCancel)
        {
            RestorePendingCardIfNeeded();
        }
        else
        {
            ClearPendingState();
        }
        ClearSelectionState();
        rollbackSteps.Clear();
        request.onCancel?.Invoke();
    }

    public bool EnterHangingState(
        CardController sourceCard,
        bool restoreOnCancel,
        Action onReady = null,
        float minimumReadyDelay = MinimumHangingDuration)
    {
        if (sourceCard == null || sourceCard.player == null)
        {
            return false;
        }

        if (pendingCard == sourceCard)
        {
            pendingRestoreOnCancel = restoreOnCancel;
            NotifyHangingReady(sourceCard, onReady, minimumReadyDelay);
            return true;
        }

        if (pendingCard != null)
        {
            return false;
        }

        pendingCard = sourceCard;
        pendingRestoreOnCancel = restoreOnCancel;
        pendingOriginalParent = sourceCard.transform.parent;
        pendingOriginalLocalPosition = sourceCard.transform.localPosition;
        pendingOriginalLocalRotation = sourceCard.transform.localRotation;
        pendingOriginalLocalScale = sourceCard.transform.localScale;
        pendingOriginalState = sourceCard.state;
        pendingOriginalHand = sourceCard.player.handController;
        pendingOriginalField = sourceCard.player.fieldController;
        pendingOriginalHandIndex = pendingOriginalHand != null ? pendingOriginalHand.handCards.IndexOf(sourceCard) : -1;
        pendingOriginalFieldIndex = pendingOriginalField != null ? pendingOriginalField.fieldCards.IndexOf(sourceCard) : -1;
        pendingOriginalShowBack = sourceCard.cardDisplay != null && sourceCard.cardDisplay.back != null && sourceCard.cardDisplay.back.activeSelf;
        SortingGroup sourceSortingGroup = sourceCard.GetComponent<SortingGroup>();
        pendingOriginalHadSortingGroup = sourceSortingGroup != null;
        pendingOriginalSortingOrder = pendingOriginalHadSortingGroup ? sourceSortingGroup.sortingOrder : 0;

        if (pendingOriginalHand != null && pendingOriginalHandIndex >= 0)
        {
            pendingOriginalHand.RemoveCard(sourceCard);
        }
        else if (pendingOriginalField != null && pendingOriginalFieldIndex >= 0)
        {
            pendingOriginalField.RemoveCard(sourceCard);
        }

        EnsureEffectPoint();
        Transform hangingParent = effectPoint != null ? effectPoint.transform : sourceCard.player.transform;
        Vector3 targetHangingLocalPosition = effectPoint != null ? Vector3.zero : hangingLocalPosition;

        sourceCard.transform.SetParent(hangingParent, true);
        ApplyHangingStateImmediately(sourceCard);
        if (sourceCard.cardDisplay != null)
        {
            sourceCard.cardDisplay.ShowBack(false);
            sourceCard.cardDisplay.UpdateCard();
        }

        AnimeSequence sequence = AnimeManager.CreateSequence();
        bool hasAnimation = false;
        hasAnimation |= AnimeManager.GroupLocalPosition(sequence, sourceCard.transform, "Hanging", targetHangingLocalPosition, 0.25f);
        hasAnimation |= AnimeManager.GroupLocalRotation(sequence, sourceCard.transform, "Hanging", Quaternion.identity, 0.25f);

        pendingHangingReleaseTime = Time.time + Mathf.Max(MinimumHangingDuration, hasAnimation ? 0.5f : 0f);
        if (hasAnimation)
        {
            sequence.OnComplete(() =>
            {
                ConfirmHangingStateApplied(sourceCard);
                NotifyHangingReady(sourceCard, onReady, minimumReadyDelay);
            });
        }
        else
        {
            ConfirmHangingStateApplied(sourceCard);
            NotifyHangingReady(sourceCard, onReady, minimumReadyDelay);
        }

        return true;
    }

    public void ReleaseHangingState(CardController sourceCard, Action onComplete = null)
    {
        if (sourceCard == null || pendingCard != sourceCard)
        {
            onComplete?.Invoke();
            return;
        }

        float remainingDuration = Mathf.Max(0f, pendingHangingReleaseTime - Time.time);
        if (remainingDuration > 0f)
        {
            AnimeManager.State("Hanging", $"{sourceCard.name} release delayed {remainingDuration:0.###}s");
            AnimeManager.Delay(remainingDuration, () => CompleteHangingRelease(sourceCard, onComplete));
            return;
        }

        CompleteHangingRelease(sourceCard, onComplete);
    }

    private void CompleteHangingRelease(CardController sourceCard, Action onComplete)
    {
        if (sourceCard != null && pendingCard == sourceCard)
        {
            ClearPendingState();
        }

        onComplete?.Invoke();
    }

    private void ApplyHangingStateImmediately(CardController sourceCard)
    {
        if (sourceCard == null)
        {
            return;
        }

        CardState previousState = sourceCard.state;
        sourceCard.state = CardState.Hanging;
        sourceCard.transform.localScale = hangingScale;
        AnimeManager.State("Hanging", $"{sourceCard.name} state: {previousState} -> {CardState.Hanging}");
        AnimeManager.State("Hanging", $"{sourceCard.name} localScale set to {hangingScale.x:0.###}");

        SortingGroup group = sourceCard.GetComponent<SortingGroup>();
        if (group != null && pendingOriginalHadSortingGroup)
        {
            group.sortingOrder = pendingOriginalSortingOrder + HangingSortingOrderOffset;
            AnimeManager.State("Hanging", $"{sourceCard.name} sortingOrder: {pendingOriginalSortingOrder} -> {group.sortingOrder}");
        }
    }

    private void ConfirmHangingStateApplied(CardController sourceCard)
    {
        if (sourceCard == null || pendingCard != sourceCard)
        {
            return;
        }

        if (sourceCard.state != CardState.Hanging)
        {
            Debug.LogWarning($"[Animation] HangingConfirm {sourceCard.name} state was {sourceCard.state}, reset to {CardState.Hanging}");
            sourceCard.state = CardState.Hanging;
        }

        if (AnimeManager.ShouldAnimate(sourceCard.transform.localScale, hangingScale))
        {
            Debug.LogWarning($"[Animation] HangingConfirm {sourceCard.name} scale was not {hangingScale.x:0.###}, reset now");
            sourceCard.transform.localScale = hangingScale;
        }

        SortingGroup group = sourceCard.GetComponent<SortingGroup>();
        if (group != null && pendingOriginalHadSortingGroup)
        {
            int expectedSortingOrder = pendingOriginalSortingOrder + HangingSortingOrderOffset;
            if (group.sortingOrder != expectedSortingOrder)
            {
                Debug.LogWarning($"[Animation] HangingConfirm {sourceCard.name} sortingOrder was {group.sortingOrder}, reset to {expectedSortingOrder}");
                group.sortingOrder = expectedSortingOrder;
            }
        }

        AnimeManager.State("HangingConfirm", $"{sourceCard.name} confirmed Hanging, scale {sourceCard.transform.localScale.x:0.###}");
    }

    private void NotifyHangingReady(CardController sourceCard, Action onReady, float minimumReadyDelay)
    {
        if (onReady == null)
        {
            return;
        }

        float readyDelay = Mathf.Max(MinimumHangingDuration, minimumReadyDelay);
        if (readyDelay > 0f)
        {
            AnimeManager.Delay(readyDelay, () =>
            {
                if (sourceCard != null && pendingCard == sourceCard)
                {
                    onReady.Invoke();
                }
            });
            return;
        }

        if (sourceCard != null && pendingCard == sourceCard)
        {
            onReady.Invoke();
        }
    }

    public bool IsSelectableTarget(CardController card)
    {
        return IsSelectableTargetInternal(card);
    }

    public bool IsSelectableTarget(PlayerController player)
    {
        return IsSelectableTargetInternal(player);
    }

    public bool IsSelectedTarget(CardController card)
    {
        return IsSelectedTargetInternal(card);
    }

    public bool IsSelectedTarget(PlayerController player)
    {
        return IsSelectedTargetInternal(player);
    }

    private void ExecuteRollbackStep(RollbackStep step)
    {
        if (step == null)
        {
            return;
        }

        if (step.type == RollbackStepType.SelectedTarget)
        {
            selectedTargets.Remove(step.selectedTarget);
            RemoveSelectionMarker(step.selectedTarget);
            RefreshTargetCards();
            return;
        }

        if (step.type == RollbackStepType.PlayedCard)
        {
            RollbackPlayedCard(step.playedCardState);
            return;
        }

        if (step.type == RollbackStepType.FieldCast)
        {
            RollbackFieldCast(step.fieldCastState);
        }
    }

    private void RollbackFieldCast(FieldCastState state)
    {
        if (state != null && state.card != null)
        {
            state.card.SetCastUsed(state.castUsed);
        }

        TargetSelectionRequest request = currentRequest;
        RestoreTransientSelectionState();
        if (pendingRestoreOnCancel)
        {
            RestorePendingCardIfNeeded();
        }
        else
        {
            ClearPendingState();
        }
        ClearSelectionState();
        rollbackSteps.Clear();
        request?.onCancel?.Invoke();
    }

    private void RollbackPlayedCard(PlayedCardState state)
    {
        if (state == null || state.card == null)
        {
            return;
        }

        TargetSelectionRequest request = currentRequest;
        RestoreTransientSelectionState();
        RestorePlayedCardState(state);
        ClearPendingState();
        ClearSelectionState();
        rollbackSteps.Clear();
        request?.onCancel?.Invoke();
    }

    private void RestorePlayedCardState(PlayedCardState state)
    {
        CardController card = state.card;
        PlayerController player = state.player;
        if (card == null || player == null)
        {
            return;
        }

        if (player.handController != null && player.handController.handCards.Contains(card))
        {
            player.handController.RemoveCard(card);
        }

        if (player.fieldController != null && player.fieldController.fieldCards.Contains(card))
        {
            player.fieldController.RemoveCard(card);
        }

        if (player.graveCards.Contains(card))
        {
            player.graveCards.Remove(card);
            player.RefreshGraveyardSorting();
        }

        RestoreCardRuntimeState(card, state);
        player.RestoreCostState(state.costBefore, state.maxCostBefore);

        if (state.hand != null && (state.handIndex >= 0 || state.state == CardState.Hand))
        {
            int insertIndex = state.handIndex >= 0 ? state.handIndex : state.hand.handCards.Count;
            RestoreCardTransform(card, state);
            card.state = state.state;
            if (card.cardDisplay != null)
            {
                card.cardDisplay.ShowBack(state.showBack);
            }

            state.hand.InsertCard(card, insertIndex);
            card.transform.localScale = state.localScale;
            if (card.cardDisplay != null)
            {
                card.cardDisplay.UpdateCard();
            }
            return;
        }

        if (state.field != null && state.fieldIndex >= 0)
        {
            int insertIndex = Mathf.Clamp(state.fieldIndex, 0, state.field.fieldCards.Count);
            state.field.fieldCards.Insert(insertIndex, card);
            RestoreCardTransform(card, state);
            card.state = state.state;
            state.field.RefreshField();
            return;
        }

        RestoreCardTransform(card, state);
        card.state = state.state;
        if (card.cardDisplay != null)
        {
            card.cardDisplay.ShowBack(state.showBack);
            card.cardDisplay.UpdateCard();
        }
    }

    private static void RestoreCardRuntimeState(CardController card, PlayedCardState state)
    {
        card.cost = state.cardCost;
        card.atk = state.attack;
        card.health = state.health;
        card.maxHealth = state.maxHealth;
        card.canAttack = state.canAttack;
        card.castUsed = state.castUsed;
        card.isSilence = state.isSilence;
        card.isDying = state.isDying;

        SortingGroup group = card.GetComponent<SortingGroup>();
        if (group != null && state.hadSortingGroup)
        {
            group.sortingOrder = state.sortingOrder;
        }

        if (card.cardDisplay != null)
        {
            card.cardDisplay.UpdateCard();
        }
    }

    private static void RestoreCardTransform(CardController card, PlayedCardState state)
    {
        if (state.parent != null)
        {
            card.transform.SetParent(state.parent, false);
        }

        card.transform.localPosition = state.localPosition;
        card.transform.localRotation = state.localRotation;
        card.transform.localScale = state.localScale;
    }

    private bool TrySelectTargetInternal(UnityEngine.Object target)
    {
        if (!HasActiveSelection || target == null || !availableTargets.Contains(target) || selectedTargets.Contains(target))
        {
            return false;
        }

        selectedTargets.Add(target);
        CreateSelectionMarker(target);
        rollbackSteps.Add(new RollbackStep
        {
            type = RollbackStepType.SelectedTarget,
            selectedTarget = target,
        });
        RefreshTargetCards();

        if (selectedTargets.Count >= requiredCount)
        {
            CompleteSelection();
        }

        return true;
    }

    private bool IsSelectableTargetInternal(UnityEngine.Object target)
    {
        return target != null && HasActiveSelection && availableTargets.Contains(target);
    }

    private bool IsSelectedTargetInternal(UnityEngine.Object target)
    {
        return target != null && selectedTargets.Contains(target);
    }

    private List<UnityEngine.Object> BuildUniqueCandidateList(List<UnityEngine.Object> candidates)
    {
        List<UnityEngine.Object> results = new();
        if (candidates == null)
        {
            return results;
        }

        foreach (UnityEngine.Object candidate in candidates)
        {
            if (candidate == null || results.Contains(candidate))
            {
                continue;
            }

            results.Add(candidate);
        }

        return results;
    }

    private void MoveCardToHangingState(CardController sourceCard)
    {
        EnterHangingState(sourceCard, true);
    }

    private void PresentGraveyardTargets()
    {
        EnsureSelectionObjects();
        CacheCameraState();
        MoveCameraToSelectionArea();
        CacheGraveCardStates();

        bool usingPanelContent;
        Transform layoutRoot = GetPanelContentRoot(out usingPanelContent);
        if (layoutRoot == null)
        {
            return;
        }

        panelSelectionActive = usingPanelContent;
        if (usingPanelContent && selectPanel != null)
        {
            selectPanel.SetActive(true);
        }
        else if (selectPanel != null)
        {
            layoutRoot.position = selectPanel.transform.position;
        }

        if (!usingPanelContent
            && currentRequest != null
            && currentRequest.sourceCard != null
            && currentRequest.sourceCard.player != null
            && currentRequest.sourceCard.player.graveCardParent != null)
        {
            layoutRoot.rotation = currentRequest.sourceCard.player.graveCardParent.rotation;
        }

        AnimeSequence sequence = AnimeManager.CreateSequence();
        float startX = -((availableTargets.Count - 1) * selectionSpacing * 0.5f);
        for (int i = 0; i < availableTargets.Count; i++)
        {
            CardController target = availableTargets[i] as CardController;
            if (target == null)
            {
                continue;
            }

            target.transform.SetParent(layoutRoot, true);
            int maxPerRow = Mathf.Max(1, panelMaxPerRow);
            int col = i % maxPerRow;
            int row = i / maxPerRow;
            Vector3 localPosition = usingPanelContent
                ? new Vector3(
                    panelStartLocalPosition.x + (col * panelHorizontalSpacing),
                    panelStartLocalPosition.y - (row * panelVerticalSpacing),
                    panelStartLocalPosition.z)
                : new Vector3(startX + (i * selectionSpacing), 0f, 0f);
            AnimeManager.GroupLocalPosition(sequence, target.transform, "GraveSelection", localPosition, 0.25f);

            if (target.cardDisplay != null)
            {
                target.cardDisplay.ShowBack(false);
                target.cardDisplay.UpdateCard();
                AnimeManager.GroupLocalRotation(sequence, target.transform, "GraveSelection", Quaternion.identity, 0.25f);
            }

            AnimeManager.GroupScale(sequence, target.transform, "GraveSelection", Vector3.one, 0.25f);

            SortingGroup group = target.GetComponent<SortingGroup>();
            if (group != null)
            {
                group.sortingOrder = 200 + i;
            }
        }
    }

    private Transform GetPanelContentRoot(out bool usingPanelContent)
    {
        usingPanelContent = false;
        if (selectPanel != null)
        {
            ScrollRect scrollRect = selectPanel.GetComponentInChildren<ScrollRect>();
            if (scrollRect != null && scrollRect.content != null)
            {
                usingPanelContent = true;
                return scrollRect.content;
            }

            Debug.LogWarning("[TargetManager] selectPanel has no ScrollRect content; falling back to selectObj layout.");
        }

        return selectObj != null ? selectObj.transform : null;
    }

    private void ShowSelectionObject()
    {
        EnsureSelectionObjects();
        if (selectObj == null)
        {
            return;
        }

        selectObj.SetActive(true);
        UpdateSelectionObjectPosition();
    }

    private void HideSelectionObject()
    {
        if (selectObj != null)
        {
            selectObj.SetActive(false);
        }

        if (panelSelectionActive && selectPanel != null)
        {
            selectPanel.SetActive(false);
            panelSelectionActive = false;
        }
    }

    private void UpdateSelectionObjectPosition()
    {
        if (selectObj == null || Camera.main == null)
        {
            return;
        }

        Vector3 mousePosition = Input.mousePosition;
        mousePosition.z = 25f;
        selectObj.transform.position = Camera.main.ScreenToWorldPoint(mousePosition);
    }
    private void EnsureSelectionObjects()
    {
        if (selectPanel == null)
        {
            selectPanel = GameObject.Find("SelectPanel");
        }

        if (selectObj == null)
        {
            selectObj = new GameObject("TargetSelectionRoot");
        }
    }

    private void EnsureEffectPoint()
    {
        if (effectPoint == null)
        {
            effectPoint = GameObject.Find("EffectPoint");
        }
    }

    private void CacheCameraState()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null || cameraStateCached)
        {
            return;
        }

        cameraStateCached = true;
        originalCameraPosition = mainCamera.transform.position;
        originalCameraRotation = mainCamera.transform.rotation;
    }

    private void MoveCameraToSelectionArea()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null || graveSelectionCameraTarget == null)
        {
            return;
        }

        AnimeSequence sequence = AnimeManager.CreateSequence();
        AnimeManager.GroupPosition(sequence, mainCamera.transform, "GraveCamera", graveSelectionCameraTarget.position, 0.3f);
        AnimeManager.GroupRotation(sequence, mainCamera.transform, "GraveCamera", graveSelectionCameraTarget.rotation, 0.3f);
    }

    private void RestoreTransientSelectionState()
    {
        if (currentRequest != null && currentRequest.zone == TargetSelectionZone.Graveyard)
        {
            RestoreGraveyardTargets();
            RestoreCameraState();
        }
    }

    private void RestoreGraveyardTargets()
    {
        foreach (KeyValuePair<CardController, GraveCardState> pair in graveCardStates)
        {
            CardController card = pair.Key;
            GraveCardState state = pair.Value;
            if (card == null || state == null || state.parent == null)
            {
                continue;
            }

            card.transform.SetParent(state.parent, false);
            card.transform.localPosition = state.localPosition;
            card.transform.localRotation = state.localRotation;
            card.transform.localScale = state.localScale;

            if (card.cardDisplay != null)
            {
                card.cardDisplay.ShowBack(state.showBack);
                card.cardDisplay.UpdateCard();
            }

            SortingGroup group = card.GetComponent<SortingGroup>();
            if (group != null)
            {
                group.sortingOrder = state.sortingOrder;
            }
        }

        graveCardStates.Clear();
    }

    private void RestoreCameraState()
    {
        if (!cameraStateCached)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            cameraStateCached = false;
            return;
        }

        AnimeSequence sequence = AnimeManager.CreateSequence();
        AnimeManager.GroupPosition(sequence, mainCamera.transform, "GraveCameraReturn", originalCameraPosition, 0.3f);
        AnimeManager.GroupRotation(sequence, mainCamera.transform, "GraveCameraReturn", originalCameraRotation, 0.3f);

        cameraStateCached = false;
    }

    private void CacheGraveCardStates()
    {
        graveCardStates.Clear();
        foreach (UnityEngine.Object availableTarget in availableTargets)
        {
            CardController target = availableTarget as CardController;
            if (target == null)
            {
                continue;
            }

            SortingGroup group = target.GetComponent<SortingGroup>();
            graveCardStates[target] = new GraveCardState
            {
                parent = target.transform.parent,
                localPosition = target.transform.localPosition,
                localRotation = target.transform.localRotation,
                localScale = target.transform.localScale,
                showBack = target.cardDisplay != null && target.cardDisplay.back != null && target.cardDisplay.back.activeSelf,
                sortingOrder = group != null ? group.sortingOrder : 0,
            };
        }
    }

    private void CompleteSelection()
    {
        if (!HasActiveSelection)
        {
            return;
        }

        TargetSelectionRequest request = currentRequest;
        List<UnityEngine.Object> results = new(selectedTargets);
        RemoveSelectedTargetRollbackSteps();
        RestoreTransientSelectionState();
        ClearSelectionState();
        request.onComplete?.Invoke(results);
    }

    private void RestorePendingCardIfNeeded()
    {
        if (pendingCard == null)
        {
            return;
        }

        CardController cardToRestore = pendingCard;
        RestorePendingSortingGroup(cardToRestore);
        if (pendingOriginalHand != null && pendingOriginalHandIndex >= 0)
        {
            cardToRestore.transform.localScale = Vector3.one;
            if (cardToRestore.cardDisplay != null)
            {
                cardToRestore.cardDisplay.ShowBack(pendingOriginalShowBack);
            }
            pendingOriginalHand.InsertCard(cardToRestore, pendingOriginalHandIndex);
        }
        else if (pendingOriginalField != null && pendingOriginalFieldIndex >= 0)
        {
            if (!pendingOriginalField.fieldCards.Contains(cardToRestore))
            {
                int insertIndex = Mathf.Clamp(pendingOriginalFieldIndex, 0, pendingOriginalField.fieldCards.Count);
                pendingOriginalField.fieldCards.Insert(insertIndex, cardToRestore);
            }

            cardToRestore.transform.SetParent(pendingOriginalParent, false);
            cardToRestore.transform.localPosition = pendingOriginalLocalPosition;
            cardToRestore.transform.localRotation = pendingOriginalLocalRotation;
            cardToRestore.transform.localScale = pendingOriginalLocalScale;
            cardToRestore.state = pendingOriginalState;
            if (cardToRestore.cardDisplay != null)
            {
                cardToRestore.cardDisplay.ShowBack(pendingOriginalShowBack);
                cardToRestore.cardDisplay.UpdateCard();
            }
            pendingOriginalField.RefreshField();
        }
        else if (pendingOriginalParent != null)
        {
            cardToRestore.transform.SetParent(pendingOriginalParent, false);
            cardToRestore.transform.localPosition = pendingOriginalLocalPosition;
            cardToRestore.transform.localRotation = pendingOriginalLocalRotation;
            cardToRestore.transform.localScale = pendingOriginalLocalScale;
            cardToRestore.state = pendingOriginalState;
            if (cardToRestore.cardDisplay != null)
            {
                cardToRestore.cardDisplay.ShowBack(pendingOriginalShowBack);
                cardToRestore.cardDisplay.UpdateCard();
            }
        }

        ClearPendingState();
    }

    private void RestorePendingSortingGroup(CardController cardToRestore)
    {
        if (cardToRestore == null || !pendingOriginalHadSortingGroup)
        {
            return;
        }

        SortingGroup group = cardToRestore.GetComponent<SortingGroup>();
        if (group != null)
        {
            group.sortingOrder = pendingOriginalSortingOrder;
        }
    }

    private void ClearSelectionState()
    {
        availableTargets.Clear();
        selectedTargets.Clear();
        currentRequest = null;
        requiredCount = 1;
        ClearSelectionMarkers();
        if (pendingRestoreOnCancel)
        {
            ClearPendingState();
        }
        HideSelectionObject();
        RefreshTargetCards();
    }

    private void ClearPendingState()
    {
        pendingCard = null;
        pendingOriginalHand = null;
        pendingOriginalField = null;
        pendingOriginalParent = null;
        pendingOriginalLocalPosition = Vector3.zero;
        pendingOriginalLocalRotation = Quaternion.identity;
        pendingOriginalLocalScale = Vector3.one;
        pendingOriginalState = CardState.Deck;
        pendingOriginalShowBack = false;
        pendingOriginalHadSortingGroup = false;
        pendingOriginalSortingOrder = 0;
        pendingOriginalHandIndex = -1;
        pendingOriginalFieldIndex = -1;
        pendingHangingReleaseTime = 0f;
        pendingRestoreOnCancel = true;
    }

    private void RefreshTargetCards()
    {
        HashSet<CardController> cardsToRefresh = new();
        foreach (UnityEngine.Object availableTarget in availableTargets)
        {
            if (availableTarget is CardController card)
            {
                cardsToRefresh.Add(card);
            }
        }

        foreach (UnityEngine.Object selectedTarget in selectedTargets)
        {
            if (selectedTarget is CardController card)
            {
                cardsToRefresh.Add(card);
            }
        }

        foreach (CardController card in cardsToRefresh)
        {
            if (card.cardDisplay != null)
            {
                card.cardDisplay.UpdateCard();
            }
        }
    }

    private void RemoveSelectedTargetRollbackSteps()
    {
        rollbackSteps.RemoveAll(step => step != null && step.type == RollbackStepType.SelectedTarget);
    }

    private void UpdateSelectionMarkers()
    {
        for (int i = selectionMarkers.Count - 1; i >= 0; i--)
        {
            SelectionMarkerState marker = selectionMarkers[i];
            if (marker == null || marker.target == null || marker.instance == null)
            {
                RemoveSelectionMarkerAt(i);
                continue;
            }

            marker.instance.transform.Rotate(0f, 0f, -SelectionMarkerRotationSpeed * Time.deltaTime, Space.Self);
        }
    }

    private void CreateSelectionMarker(UnityEngine.Object target)
    {
        Transform targetTransform = GetTargetTransform(target);
        if (targetTransform == null || selectObj == null)
        {
            return;
        }

        GameObject instance = Instantiate(selectObj);
        instance.name = $"{selectObj.name}_Marker";
        instance.transform.SetParent(targetTransform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * SelectionMarkerBaseScale;
        DisableRaycastInterference(instance);
        instance.SetActive(true);

        selectionMarkers.Add(new SelectionMarkerState
        {
            target = target,
            instance = instance,
        });

        AnimeManager.Scale(
            instance.transform,
            "SelectionMarker",
            Vector3.one * SelectionMarkerPulseScale,
            SelectionMarkerPulseDuration,
            2,
            true,
            false);
    }

    private bool RemoveSelectionMarker(UnityEngine.Object target)
    {
        for (int i = selectionMarkers.Count - 1; i >= 0; i--)
        {
            SelectionMarkerState marker = selectionMarkers[i];
            if (marker == null || marker.target != target)
            {
                continue;
            }

            RemoveSelectionMarkerAt(i);
            return true;
        }

        return false;
    }

    private void RemoveSelectionMarkerAt(int index)
    {
        if (index < 0 || index >= selectionMarkers.Count)
        {
            return;
        }

        SelectionMarkerState marker = selectionMarkers[index];
        selectionMarkers.RemoveAt(index);
        if (marker != null && marker.instance != null)
        {
            Destroy(marker.instance);
        }
    }

    private void ClearSelectionMarkers()
    {
        for (int i = selectionMarkers.Count - 1; i >= 0; i--)
        {
            RemoveSelectionMarkerAt(i);
        }
    }

    private static Transform GetTargetTransform(UnityEngine.Object target)
    {
        if (target is PlayerController player
            && GM.Ins != null
            && GM.Ins.BM != null
            && GM.Ins.BM.TryGetPlayerIconTarget(player, out Transform playerIconTarget))
        {
            return playerIconTarget;
        }

        if (target is Component component)
        {
            return component.transform;
        }

        return null;
    }

    private static void DisableRaycastInterference(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (Collider2D collider2D in root.GetComponentsInChildren<Collider2D>(true))
        {
            collider2D.enabled = false;
        }
    }
}
