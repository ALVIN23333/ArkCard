using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DataManager:MonoBehaviour
{
    public CardListSO so;
    private void Awake()
    {
        so=Resources.Load<CardListSO>("ArkCardsDatabase");
    }
}
