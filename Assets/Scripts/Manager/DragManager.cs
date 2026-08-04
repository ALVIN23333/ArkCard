using System.Collections.Generic;
using UnityEngine;

public class DragManager : MonoBehaviour
{
    public GameObject dragObject;

    private bool isDragging;
    private bool isAttackDragging;
    private bool isTargetSelectionDragging;
    private CardController card;
    private FieldController fieldController;
    private Vector3 offset;

    private void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            HandleRightClick();
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryStartDragFromMouse();
        }

        if (Input.GetMouseButtonUp(0) && card != null)
        {
            OnDragEnd();
        }

        if (isDragging)
        {
            OnDrag();
        }
    }

    private void TryStartDragFromMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
        if (hits.Length == 0)
        {
            return;
        }

        if (TrySelectActiveTargetFromHits(hits))
        {
            return;
        }

        CardController cardController = GetClosestCardFromHits(hits);
        if (cardController == null)
        {
            return;
        }

        card = cardController;
        OnDragStart();
    }

    private CardController GetClosestCardFromHits(RaycastHit[] hits)
    {
        CardController closestCard = null;
        float minDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.gameObject == gameObject || hit.distance >= minDistance)
            {
                continue;
            }

            CardController hitCard = hit.collider.GetComponentInParent<CardController>();
            if (hitCard == null)
            {
                continue;
            }

            closestCard = hitCard;
            minDistance = hit.distance;
        }

        return closestCard;
    }

    private bool TrySelectActiveTargetFromHits(RaycastHit[] hits)
    {
        TargetManager targetManager = GM.Ins != null && GM.Ins.BM != null ? GM.Ins.BM.TM : null;
        if (targetManager == null || !targetManager.HasActiveSelection)
        {
            return false;
        }

        UnityEngine.Object selectedTarget = null;
        float minDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.gameObject == gameObject || hit.distance >= minDistance)
            {
                continue;
            }

            CardController hitCard = hit.collider.GetComponentInParent<CardController>();
            if (hitCard != null)
            {
                if (targetManager.IsSelectableTarget(hitCard))
                {
                    selectedTarget = hitCard;
                    minDistance = hit.distance;
                }

                continue;
            }

            PlayerController hitPlayer = hit.collider.GetComponentInParent<PlayerController>();
            if (hitPlayer != null && targetManager.IsSelectableTarget(hitPlayer))
            {
                selectedTarget = hitPlayer;
                minDistance = hit.distance;
            }
        }

        if (selectedTarget is CardController targetCard)
        {
            return targetManager.TrySelectTarget(targetCard);
        }

        if (selectedTarget is PlayerController targetPlayer)
        {
            return targetManager.TrySelectTarget(targetPlayer);
        }

        return false;
    }

    private void OnDragStart()
    {
        TargetManager targetManager = GM.Ins != null ? GM.Ins.BM.TM : null;
        if (targetManager != null && targetManager.HasActiveSelection)
        {
            if (card != null && targetManager.IsSelectableTarget(card))
            {
                targetManager.TrySelectTarget(card);
                ClearDragState(false);
                return;
            }

            if (targetManager.HasPendingCard && card == targetManager.PendingCard)
            {
                isDragging = true;
                isTargetSelectionDragging = true;
                UpdateDragObject(GetMouseWorldPosition());
                return;
            }

            if (card == null || card != targetManager.PendingCard)
            {
                ClearDragState(false);
                return;
            }
        }

        if (GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.IsGameOver || GM.Ins.BM.IsTurnTransitioning)
        {
            ClearDragState();
            return;
        }

        if (card == null || card.player == null || !card.player.isMainPlayer)
        {
            ClearDragState();
            return;
        }

        Vector3 worldPosition = GetMouseWorldPosition();
        if (card.state == CardState.Hand)
        {
            isDragging = true;
            isAttackDragging = false;
            isTargetSelectionDragging = false;
            offset = card.transform.position - worldPosition;
            return;
        }

        if (card.state == CardState.Field
            && card.player.isInTurn
            && GM.Ins != null
            && GM.Ins.BM != null
            && GM.Ins.BM.TryUseFieldCast(card))
        {
            ClearDragState(false);
            return;
        }

        if (card.state == CardState.Field && card.player.isInTurn && card.canAttack)
        {
            isDragging = true;
            isAttackDragging = true;
            isTargetSelectionDragging = false;
            card.SetSelected(true);
            UpdateDragObject(worldPosition);
            return;
        }

        ClearDragState();
    }

    private void OnDrag()
    {
        Vector3 worldPosition = GetMouseWorldPosition();
        if (isAttackDragging || isTargetSelectionDragging)
        {
            UpdateDragObject(worldPosition);
            return;
        }

        if (card != null)
        {
            card.transform.position = worldPosition + offset;
        }
    }

    private void OnDragEnd()
    {
        if (card == null)
        {
            ClearDragState(false);
            return;
        }

        if (isTargetSelectionDragging)
        {
            ResolveTargetSelection();
            ClearDragState(false);
            return;
        }

        if (card.player == null
            || !card.player.isInTurn
            || GM.Ins == null
            || GM.Ins.BM == null
            || GM.Ins.BM.IsGameOver
            || GM.Ins.BM.IsTurnTransitioning)
        {
            ResetDraggedCardPosition();
            ClearDragState();
            return;
        }

        if (card.state == CardState.Hand)
        {
            ResolvePlayCard();
            ClearDragState();
            return;
        }

        if (isAttackDragging)
        {
            ResolveAttack();
        }

        ClearDragState();
    }

    private void ResolvePlayCard()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
        fieldController = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.gameObject == gameObject)
            {
                continue;
            }

            fieldController = hit.collider.GetComponentInParent<FieldController>();
            if (fieldController == null)
            {
                continue;
            }

            if (card.cardData.cardType == CardType.Minion)
            {
                if (fieldController.player != card.player || !GM.Ins.BM.CanUseMinion(card, fieldController))
                {
                    break;
                }

                PlayerController owner = card.player;
                int costBefore = owner.cost;
                int maxCostBefore = owner.maxCost;
                if (!card.player.SpendCost(card.cost))
                {
                    break;
                }

                GM.Ins.BM.TM?.RegisterPlayedCardRollback(card, owner, costBefore, maxCostBefore);
                ResolveMinionPlay(card, fieldController);
                return;
            }

            if (card.cardData.cardType == CardType.SPELL)
            {
                if (!GM.Ins.BM.CanUseSpell(card))
                {
                    break;
                }

                PlayerController owner = card.player;
                int costBefore = owner.cost;
                int maxCostBefore = owner.maxCost;
                if (!card.player.SpendCost(card.cost))
                {
                    break;
                }

                GM.Ins.BM.TM?.RegisterPlayedCardRollback(card, owner, costBefore, maxCostBefore);
                ResolveSpellPlay(card);
                return;
            }
        }

        ResetDraggedCardPosition();
    }

    private void ResolveMinionPlay(CardController sourceCard, FieldController targetField)
    {
        if (sourceCard == null || targetField == null)
        {
            return;
        }

        sourceCard.transform.localScale = Vector3.one;
        sourceCard.player.handController.RemoveCard(sourceCard);
        targetField.AddCard(sourceCard);
        TargetManager targetManager = GM.Ins != null && GM.Ins.BM != null ? GM.Ins.BM.TM : null;
        AnimeManager.Delay(AnimeManager.FieldRefreshDuration, () =>
        {
            if (sourceCard == null || sourceCard.state != CardState.Field)
            {
                targetManager?.ClearPlayedCardRollback(sourceCard);
                return;
            }

            GM.Ins.BM.EM.TriggerCardEffect(sourceCard, TriggerType.Enter, null, () =>
            {
                targetManager?.ClearPlayedCardRollback(sourceCard);
            });
        });
    }

    private void ResolveSpellPlay(CardController sourceCard)
    {
        if (sourceCard == null)
        {
            return;
        }

        TargetManager targetManager = GM.Ins != null && GM.Ins.BM != null ? GM.Ins.BM.TM : null;
        void TriggerSpellAfterHangingReady()
        {
            GM.Ins.BM.EM.TriggerSpellEffect(sourceCard, null, () =>
            {
                if (sourceCard != null && sourceCard.player != null)
                {
                    if (targetManager != null)
                    {
                        bool shouldSendToGraveyard = targetManager.PendingCard == sourceCard;
                        targetManager.ReleaseHangingState(sourceCard, () =>
                        {
                            if (shouldSendToGraveyard && sourceCard != null && sourceCard.player != null)
                            {
                                sourceCard.player.SendCardToGraveyard(sourceCard);
                            }

                            targetManager.ClearPlayedCardRollback(sourceCard);
                        });
                    }
                    else
                    {
                        sourceCard.player.SendCardToGraveyard(sourceCard);
                    }
                }
            });
        }

        if (targetManager == null || !targetManager.EnterHangingState(sourceCard, false, TriggerSpellAfterHangingReady))
        {
            TriggerSpellAfterHangingReady();
        }
    }
    private void ResolveTargetSelection()
    {
        if (GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.TM == null)
        {
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        TrySelectActiveTargetFromHits(Physics.RaycastAll(ray, 100f));
    }

    private void ResolveAttack()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f);

        CardController targetCard = null;
        PlayerController targetPlayer = null;
        float minDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.gameObject == gameObject)
            {
                continue;
            }

            CardController hitCard = hit.collider.GetComponentInParent<CardController>();
            if (hitCard != null && hitCard != card && hitCard.player != card.player && hit.distance < minDistance)
            {
                targetCard = hitCard;
                targetPlayer = null;
                minDistance = hit.distance;
                continue;
            }

            PlayerController hitPlayer = hit.collider.GetComponentInParent<PlayerController>();
            if (hitPlayer != null && hitPlayer != card.player && hit.distance < minDistance)
            {
                targetCard = null;
                targetPlayer = hitPlayer;
                minDistance = hit.distance;
            }
        }

        if (targetCard != null)
        {
            GM.Ins.BM.ResolveMinionAttack(card, targetCard);
            return;
        }

        if (targetPlayer != null)
        {
            GM.Ins.BM.ResolvePlayerAttack(card, targetPlayer);
        }
    }

    private void HandleRightClick()
    {
        if (GM.Ins != null && GM.Ins.BM != null && GM.Ins.BM.TM != null && GM.Ins.BM.TM.UndoLastSelectionOrPending())
        {
            ClearDragState(false);
            return;
        }

        if (!isDragging)
        {
            return;
        }

        if (card != null && card.state == CardState.Hand)
        {
            ResetDraggedCardPosition();
        }

        ClearDragState();
    }

    private void ResetDraggedCardPosition()
    {
        if (card != null && card.state == CardState.Hand && card.player != null && card.player.handController != null)
        {
            card.player.handController.RefreshHand();
        }
    }

    private void UpdateDragObject(Vector3 worldPosition)
    {
        if (dragObject == null)
        {
            return;
        }

        dragObject.SetActive(true);
        dragObject.transform.position = worldPosition;
    }

    private Vector3 GetMouseWorldPosition()
    {
        Vector3 mousePosition = Input.mousePosition;
        mousePosition.z = 25f;
        return Camera.main.ScreenToWorldPoint(mousePosition);
    }

    private void ClearDragState(bool clearSelected = true)
    {
        if (card != null && clearSelected)
        {
            card.SetSelected(false);
        }

        if (dragObject != null)
        {
            dragObject.SetActive(false);
        }

        isDragging = false;
        isAttackDragging = false;
        isTargetSelectionDragging = false;
        card = null;
        fieldController = null;
    }
}
