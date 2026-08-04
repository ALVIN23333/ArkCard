using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class HandController : MonoBehaviour
{
    [SerializeField]
    private float anglegap;
    [SerializeField]
    private float radius;
    [SerializeField]
    private float centerY;

    public List<CardController> handCards = new List<CardController>();
    private AnimeSequence sequence;

    public void AddCard(CardController card)
    {
        InsertCard(card, handCards.Count);
    }

    public void InsertCard(CardController card, int index)
    {
        if (card == null)
        {
            return;
        }

        index = Mathf.Clamp(index, 0, handCards.Count);
        if (handCards.Contains(card))
        {
            handCards.Remove(card);
            index = Mathf.Clamp(index, 0, handCards.Count);
        }

        handCards.Insert(index, card);
        card.transform.SetParent(transform, true);
        card.state = CardState.Hand;
        if (card.cardDisplay != null)
        {
            card.cardDisplay.UpdateCard();
        }
        RefreshHand();
    }

    public void RemoveCard(CardController card)
    {
        handCards.Remove(card);
        RefreshHand();
    }

    public void RefreshHand()
    {
        if (sequence != null && sequence.IsAlive)
        {
            sequence.Stop();
        }
        sequence = AnimeManager.CreateSequence();

        int count = handCards.Count;
        float totalAngle = (count - 1) * anglegap;
        float startAngle = -(totalAngle / 2);
        for (int i = 0; i < count; i++)
        {
            if (handCards[i] == null)
            {
                continue;
            }
            float curangle = startAngle + (i * anglegap);
            Vector3 targetRotation = new Vector3(0, 0, -curangle);
            float rad = Mathf.Deg2Rad * curangle;
            Vector3 targetPosition = new Vector3(
                radius * Mathf.Sin(rad),
                radius * Mathf.Cos(rad) - centerY,
                0
            );

            Transform cardTransform = handCards[i].transform;
            int sortIndex = 50 + i;
            SortingGroup group = cardTransform.gameObject.GetComponent<SortingGroup>();
            CardController cardController = handCards[i];
            Quaternion targetLocalRotation = Quaternion.Euler(targetRotation);
            AnimeManager.GroupLocalPosition(sequence, cardTransform, "HandRefresh", targetPosition, 0.4f);
            AnimeManager.GroupLocalRotation(sequence, cardTransform, "HandRefresh", targetLocalRotation, 0.2f);
            AnimeManager.GroupScale(sequence, cardTransform, "HandRefresh", Vector3.one, 0.2f);
            group.sortingOrder = sortIndex;
            if (cardController != null && cardController.player != null && cardController.player.isMainPlayer)
            {
                cardController.cardDisplay.ShowBack(false);
            }
        }
    }
}
