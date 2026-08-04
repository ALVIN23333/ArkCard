using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Rendering;

public class PlayerController : MonoBehaviour
{
    [Header("Player Info")]
    public int playId;
    public int health;
    public int maxHealth = GameConst.initalHealth;
    public int cost;
    public int maxCost;
    public bool isMainPlayer;
    public bool isInTurn;

    [HideInInspector]
    public List<CardController> deckCards = new List<CardController>();
    [HideInInspector]
    public List<CardController> graveCards = new List<CardController>();
    private DeckData deckData;
    private AnimeSequence sequence;

    [Header("GameObject References")]
    public HandController handController;
    public FieldController fieldController;
    public Transform deckCardParent;
    public Transform graveCardParent;
    private GameObject prefab;

    [Header("UI References")]
    public TMP_Text costText;
    public TMP_Text healthText;
    public void Init()
    {
        health = maxHealth;
        cost = 0;
        maxCost = 0;
        isInTurn = false;
        deckData = new DeckData();
        prefab=GM.Ins.BM.cardPrefab;
        if(fieldController != null &&fieldController.player==null)
            fieldController.player = this;

        CardListSO cardList = GM.Ins.DM.so;
        if (cardList == null)
        {
            cardList = Resources.Load<CardListSO>("ArkCardsDatabase");
        }

        for(int i = 0; i < 2; i++)
        {
            for(int j = 1001;j<1011;j++)
            {
                deckData.deck.Add(j);
            }
        }
        deckData.deck.AddRange(new List<int>() { 1101, 1102, 1103,1104 });
        deckData.deck.AddRange(new List<int>() { 1101, 1102, 1103,1104 });
        foreach (int i in deckData.deck)
        {
            CardData data = cardList != null ? cardList.GetData(i) : null;
            if (data == null)
            {
                continue;
            }
            CardController card = Instantiate(prefab, deckCardParent).GetComponent<CardController>();
            card.Init(data,this);
            card.state = CardState.Deck;
            card.cardDisplay.ShowBack();
            deckCards.Add(card);
        }
        shuffleDeck();
        UpdateCostUI();
        UpdateHealthUI();
    }
    public void shuffleDeck()
    {
        for (int i = 0; i < deckCards.Count; i++)
        {
            int rad = Random.Range(0, deckCards.Count);
            var temp = deckCards[i];
            deckCards[i] = deckCards[rad];
            deckCards[rad] = temp;
        }
    }
    public virtual void StartTurn()
    {
        isInTurn = true;
        if (maxCost < GameConst.costMax)
            maxCost++;
        cost = maxCost;
        UpdateCostUI();
        RefreshFieldCards();
    }
    public virtual void EndTurn()
    {
        isInTurn = false;
        RefreshFieldCards();
    }
    private void UpdateCostUI()
    {
        costText.text = $"{cost}/{maxCost}";
        Vector3 targetScale = Vector3.one * 1.2f;
        AnimeManager.Scale(costText.transform, "CostUI", targetScale, 0.2f, 2, true);
    }
    private void UpdateHealthUI()
    {
        healthText.text = $"{health}";
        Vector3 targetScale = Vector3.one * 1.2f;
        AnimeManager.Scale(healthText.transform, "HealthUI", targetScale, 0.2f, 2, true);
    }
    public void Damage(int value)
    {
        if (value <= 0)
        {
            return;
        }

        health -= value;
        UpdateHealthUI();
    }
    public void Heal(int value)
    {
        if (value <= 0)
        {
            return;
        }

        health += value;
        if (health > maxHealth)
        {
            health = maxHealth;
        }
        UpdateHealthUI();
    }

    public void AddMaxCost(int value)
    {
        if (value <= 0)
        {
            return;
        }

        maxCost += value;
        if (maxCost > GameConst.costMax)
        {
            maxCost = GameConst.costMax;
        }
        UpdateCostUI();
    }
    public void AddCost(int value)
    {
        if (value <= 0)
        {
            return;
        }

        cost += value;
        UpdateCostUI();
    }

    public bool SpendCost(int value)
    {
        if (value <= 0)
        {
            return true;
        }

        if (cost < value)
        {
            return false;
        }

        cost -= value;
        UpdateCostUI();
        return true;
    }

    public void RestoreCostState(int costValue, int maxCostValue)
    {
        cost = costValue;
        maxCost = maxCostValue;
        UpdateCostUI();
    }

    public void SendCardToGraveyard(CardController card)
    {
        if (card == null)
        {
            return;
        }
        if(sequence == null || !sequence.IsAlive)
        {
            sequence = AnimeManager.CreateSequence();
        }
        if (handController != null && handController.handCards.Contains(card))
        {
            handController.RemoveCard(card);
        }

        if (fieldController != null && fieldController.fieldCards.Contains(card))
        {
            fieldController.RemoveCard(card);
        }

        if (!graveCards.Contains(card))
        {
            graveCards.Add(card);
        }

        RefreshGraveyardSorting();
        card.transform.SetParent(graveCardParent,true);
        card.state = CardState.Graveyard;
        card.ResetCard();
        card.transform.localScale = Vector3.one;
        if (card.cardDisplay != null)
        {
            card.cardDisplay.UpdateCard();
        }
        AnimeManager.GroupLocalPosition(sequence, card.transform, "Graveyard", Vector3.zero, 0.5f);
        AnimeManager.GroupLocalRotation(sequence, card.transform, "Graveyard", Quaternion.identity, 0.3f);
    }


    public void RefreshGraveyardSorting()
    {
        for (int i = 0; i < graveCards.Count; i++)
        {
            CardController graveCard = graveCards[i];
            if (graveCard == null)
            {
                continue;
            }

            SortingGroup group = graveCard.GetComponent<SortingGroup>();
            if (group != null)
            {
                group.sortingOrder = 10 + i;
            }
        }
    }
    private void RefreshFieldCards()
    {
        if (fieldController == null)
        {
            return;
        }

        foreach (CardController card in fieldController.fieldCards)
        {
            if (card != null && card.cardDisplay != null)
            {
                card.cardDisplay.UpdateCard();
            }
        }
    }
}
