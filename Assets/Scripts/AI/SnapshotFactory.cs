using System.Collections.Generic;
using UnityEngine;

public static class SnapshotFactory
{
    private const int DefaultBeliefCopiesPerCard = 2;

    public static bool TryCreate(
        BattleManager battleManager,
        int aiPlayerIndex,
        out BattleStateSnapshot snapshot,
        out string error,
        CardListSO opponentBeliefPool = null)
    {
        snapshot = null;
        error = string.Empty;
        if (battleManager == null)
        {
            error = "BattleManager is missing.";
            return false;
        }
        if (battleManager.CurrentPlayer == null)
        {
            error = "BattleManager.CurrentPlayer is missing.";
            return false;
        }
        if (battleManager.players == null || battleManager.players.Count < 2)
        {
            error = "At least two players are required to build an AI snapshot.";
            return false;
        }
        if (aiPlayerIndex < 0 || aiPlayerIndex >= battleManager.players.Count)
        {
            error = "aiPlayerIndex is out of range.";
            return false;
        }

        BattleStateSnapshot result = new()
        {
            CurrentPlayerIndex = battleManager.players.IndexOf(battleManager.CurrentPlayer),
            IsGameOver = battleManager.IsGameOver,
            IsTurnEnded = false,
        };

        if (result.CurrentPlayerIndex < 0)
        {
            error = "CurrentPlayer is not present in BattleManager.players.";
            return false;
        }

        for (int playerIndex = 0; playerIndex < battleManager.players.Count; playerIndex++)
        {
            PlayerController player = battleManager.players[playerIndex];
            if (player == null)
            {
                error = $"Player at index {playerIndex} is missing.";
                return false;
            }

            PlayerStateSnapshot playerSnapshot = new()
            {
                PlayerIndex = playerIndex,
                IsMainPlayer = player.isMainPlayer,
                Health = player.health,
                MaxHealth = player.maxHealth,
                Cost = player.cost,
                MaxCost = player.maxCost,
            };

            if (playerIndex == aiPlayerIndex)
            {
                CopyCards(player.handController != null ? player.handController.handCards : null, playerIndex, playerSnapshot.Hand);
                playerSnapshot.HiddenDeckCount = player.deckCards != null ? player.deckCards.Count : 0;
                CopyCards(player.deckCards, playerIndex, playerSnapshot.DeckRemaining);
            }
            else
            {
                playerSnapshot.HandIsHidden = true;
                playerSnapshot.HiddenHandCount = player.handController != null && player.handController.handCards != null
                    ? player.handController.handCards.Count
                    : 0;
                playerSnapshot.HiddenDeckCount = player.deckCards != null ? player.deckCards.Count : 0;
                FillBeliefPool(opponentBeliefPool, playerSnapshot.HiddenCardPool);
            }

            CopyCards(player.fieldController != null ? player.fieldController.fieldCards : null, playerIndex, playerSnapshot.Field);
            CopyCards(player.graveCards, playerIndex, playerSnapshot.Graveyard);
            result.Players.Add(playerSnapshot);
        }

        snapshot = result;
        return true;
    }

    private static void FillBeliefPool(CardListSO source, List<CardData> destination)
    {
        CardListSO database = source;
        if (database == null && GM.Ins != null && GM.Ins.DM != null && GM.Ins.DM.so != null)
        {
            database = GM.Ins.DM.so;
        }
        if (database == null)
        {
            database = Resources.Load<CardListSO>("ArkCardsDatabase");
        }
        if (database == null || database.cards == null)
        {
            return;
        }

        for (int copy = 0; copy < DefaultBeliefCopiesPerCard; copy++)
        {
            foreach (CardData card in database.cards)
            {
                if (card != null)
                {
                    destination.Add(card);
                }
            }
        }
    }

    private static void CopyCards(List<CardController> source, int ownerIndex, List<CardStateSnapshot> destination)
    {
        if (source == null)
        {
            return;
        }
        foreach (CardController card in source)
        {
            if (card == null || card.cardData == null)
            {
                continue;
            }
            destination.Add(new CardStateSnapshot
            {
                RuntimeId = card.GetInstanceID(),
                OwnerIndex = ownerIndex,
                State = card.state,
                Data = card.cardData,
                Cost = card.cost,
                Attack = card.atk,
                Health = card.health,
                MaxHealth = card.maxHealth,
                CanAttack = card.canAttack,
                CanAttackPlayer = card.canAttackPlayer,
                AttacksRemaining = card.attackCount,
                IsStealth = card.isStealth,
                HolyShield = card.holyShieldCount,
                CastUsed = card.castUsed,
                IsSilence = card.isSilence,
                IsDying = card.isDying,
            });
        }
    }
}
