using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DeckListDatabase", menuName = "ArkCards/Deck List SO")]
public class DeckListSO : ScriptableObject
{
    public List<DeckData> decks = new();
    public int playerDeckIndex = -1;
    public int aiDeckIndex = -1;

    public DeckData GetDeck(int index)
    {
        if (index < 0 || decks == null || index >= decks.Count)
        {
            return null;
        }

        return decks[index];
    }
}
