using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ArkCardsDatabase", menuName = "ArkCards/Card List SO")]
public class CardListSO : ScriptableObject
{
    public int effectSchemaVersion;
    public List<CardData> cards = new();

    public CardData GetData(int id)
    {
        foreach (var card in cards)
        {
            if (card.index == id)
            {
                return card;
            }
        }
        return null;
    }
}
