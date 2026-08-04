using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class FieldController : MonoBehaviour
{
    public PlayerController player;
    public List<CardController> fieldCards = new List<CardController>();
    public float gap = 1.5f;

    public void AddCard(CardController card)
    {
        fieldCards.Add(card);
        card.transform.SetParent(transform, true);
        card.state = CardState.Field;
        card.SetCastUsed(false);
        card.transform.localScale = Vector3.one;
        card.transform.localRotation = GetFieldCardLocalRotation();
        if (card.cardDisplay != null)
        {
            card.cardDisplay.ShowBack(false);
            card.cardDisplay.UpdateCard();
        }
        RefreshField();
    }

    public void RemoveCard(CardController card)
    {
        fieldCards.Remove(card);
        RefreshField();
    }

    public void RefreshField()
    {
        int count = fieldCards.Count;
        float totalWidth = (count - 1) * gap;
        float startX = -totalWidth / 2;
        for (int i = 0; i < count; i++)
        {
            CardController card = fieldCards[i];
            SortingGroup group = card.gameObject.GetComponent<SortingGroup>();
            group.sortingOrder = 40 + i;
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
