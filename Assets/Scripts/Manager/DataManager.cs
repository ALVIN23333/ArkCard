using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DataManager:MonoBehaviour
{
    public CardListSO so;
    public DeckListSO decks;
    private void Awake()
    {
        so=Resources.Load<CardListSO>("ArkCardsDatabase");
        decks=Resources.Load<DeckListSO>("DeckListDatabase");
    }
}
