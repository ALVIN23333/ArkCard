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
    public bool IsAIControlled;

    [HideInInspector]
    public List<CardController> deckCards = new List<CardController>();
    [HideInInspector]
    public List<CardController> graveCards = new List<CardController>();
    public DeckData deckData = new DeckData();
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
        if (deckData == null)
        {
            deckData = new DeckData();
        }
        prefab=GM.Ins.BM.cardPrefab;
        if(fieldController != null &&fieldController.player==null)
            fieldController.player = this;

        CardListSO cardList = GM.Ins.DM.so;
        if (cardList == null)
        {
            cardList = Resources.Load<CardListSO>("ArkCardsDatabase");
        }

        LoadAssignedDeck();
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

    private void LoadAssignedDeck()
    {
        DeckListSO deckDb = GM.Ins != null && GM.Ins.DM != null ? GM.Ins.DM.decks : null;
        if (deckDb == null)
        {
            deckDb = Resources.Load<DeckListSO>("DeckListDatabase");
        }

        int deckIndex = isMainPlayer
            ? (deckDb != null ? deckDb.playerDeckIndex : -1)
            : (deckDb != null ? deckDb.aiDeckIndex : -1);
        DeckData assignedDeck = deckDb != null ? deckDb.GetDeck(deckIndex) : null;
        if (assignedDeck != null)
        {
            deckData.name = assignedDeck.name;
            if (deckData.deck == null)
            {
                deckData.deck = new List<int>();
            }
            deckData.deck.Clear();
            deckData.deck.AddRange(assignedDeck.deck);
        }
        else
        {
            deckData.name = "空卡组";
            if (deckData.deck == null)
            {
                deckData.deck = new List<int>();
            }
            deckData.deck.Clear();
            Debug.LogWarning($"[PlayerController] 未找到有效卡组（index={deckIndex}），使用空卡组开局。");
        }
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
    public void DrawCard()
    {
        if (deckCards.Count <= 0)
        {
            return;
        }

        CardController card = deckCards[0];
        deckCards.RemoveAt(0);

        if (handController != null && handController.handCards.Count >= GameConst.handMax)
        {
            TargetManager targetManager = GM.Ins != null && GM.Ins.BM != null ? GM.Ins.BM.TM : null;
            if (targetManager != null)
            {
                targetManager.ShowCardHangingThenSendToGraveyard(card);
            }
            else
            {
                SendCardToGraveyard(card);
            }
            return;
        }

        handController.AddCard(card);
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

    public void SetHealth(int value)
    {
        health = Mathf.Max(0, value);
        UpdateHealthUI();
    }

    public void SetMaxHealth(int value)
    {
        maxHealth = Mathf.Max(0, value);
        UpdateHealthUI();
    }

    public void SetCost(int value)
    {
        cost = Mathf.Max(0, value);
        UpdateCostUI();
    }

    public void SetMaxCost(int value)
    {
        maxCost = Mathf.Max(0, value);
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
            card.cardDisplay.ShowBack(false);
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
