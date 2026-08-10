using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class FieldController : MonoBehaviour
{
    public PlayerController player;
    public List<CardController> fieldCards = new List<CardController>();
    public float gap = 1.5f;
    private bool layoutDirty;

    public void AddCard(CardController card)
    {
        AnimeManager.Stop(card != null ? card.transform : null);
        fieldCards.Add(card);
        card.transform.SetParent(transform, true);
        card.state = CardState.Field;
        card.SetCastUsed(false);
        card.canAttack = card.HasAnyPassive(PassiveType.Rush, PassiveType.Charge);
        card.canAttackPlayer = card.HasPassive(PassiveType.Charge);
        card.attackCount = card.HasPassive(PassiveType.Windfury) ? 2 : 1;
        card.isStealth = card.HasPassive(PassiveType.Stealth);
        card.holyShieldCount = card.HasPassive(PassiveType.HolyShield) ? 1 : 0;
        card.transform.localScale = Vector3.one;
        card.transform.localRotation = GetFieldCardLocalRotation();
        if (card.cardDisplay != null)
        {
            card.cardDisplay.ShowBack(false);
            card.cardDisplay.UpdateCard();
        }
        RefreshField();
    }

    public List<CardController> GetAdjacentCards(CardController card)
    {
        List<CardController> result = new();
        if (card == null)
        {
            return result;
        }

        int index = fieldCards.IndexOf(card);
        if (index < 0)
        {
            return result;
        }

        if (index > 0 && fieldCards[index - 1] != null)
        {
            result.Add(fieldCards[index - 1]);
        }

        if (index + 1 < fieldCards.Count && fieldCards[index + 1] != null)
        {
            result.Add(fieldCards[index + 1]);
        }

        return result;
    }

    public void RemoveCard(CardController card)
    {
        fieldCards.Remove(card);
        RefreshField();
    }

    public void RefreshField()
    {
        // 同一帧内的多次布局请求合并为一次，避免多目标效果在同步循环里
        // 产生“中间布局”的残留动画，导致卡牌停在错误的中间位置。
        layoutDirty = true;
        if (!isActiveAndEnabled)
        {
            layoutDirty = false;
            ApplyFieldLayout();
        }
    }

    private void LateUpdate()
    {
        if (!layoutDirty)
        {
            return;
        }

        layoutDirty = false;
        ApplyFieldLayout();
    }

    private void ApplyFieldLayout()
    {
        fieldCards.RemoveAll(card => card == null);
        int count = fieldCards.Count;
        float totalWidth = (count - 1) * gap;
        float startX = -totalWidth / 2;
        for (int i = 0; i < count; i++)
        {
            CardController card = fieldCards[i];
            SortingGroup group = card.gameObject.GetComponent<SortingGroup>();
            if (group != null)
            {
                group.sortingOrder = 40 + i;
            }
            Vector3 targetPosition = new Vector3(startX + (i * gap), 0, 0);
            Transform cardTransform = card.gameObject.transform;
            AnimeManager.LocalPosition(cardTransform, "FieldRefresh", targetPosition, AnimeManager.FieldRefreshDuration);

            Quaternion targetRotation = GetFieldCardLocalRotation();
            AnimeManager.LocalRotation(cardTransform, "FieldRefresh", targetRotation, AnimeManager.FieldRefreshDuration);
        }
    }

    private Quaternion GetFieldCardLocalRotation()
    {
        return Quaternion.identity;
    }
}
