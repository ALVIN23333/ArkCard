using System;
using System.Collections.Generic;

/// <summary>
/// AI 模拟效果共享原语：效果定义类通过这里操作纯数据快照。
/// </summary>
public static class SimulationEffectActions
{
    public static void DrawCards(PlayerStateSnapshot player, int count, Random random)
    {
        if (player == null || count <= 0)
        {
            return;
        }

        for (int i = 0; i < count && player.DeckRemaining.Count > 0; i++)
        {
            CardStateSnapshot card = player.DeckRemaining[0];
            player.DeckRemaining.RemoveAt(0);
            if (card == null)
            {
                i--;
                continue;
            }
            if (player.Hand.Count >= GameConst.handMax)
            {
                card.ResetRuntimeState(CardState.Graveyard);
                player.Graveyard.Add(card);
            }
            else
            {
                card.State = CardState.Hand;
                player.Hand.Add(card);
            }
        }
    }

    public static void Discard(PlayerStateSnapshot player, int count, Random random)
    {
        if (player == null || count <= 0)
        {
            return;
        }

        for (int i = 0; i < count && player.Hand.Count > 0; i++)
        {
            int bestIndex = 0;
            for (int j = 1; j < player.Hand.Count; j++)
            {
                if (player.Hand[j] != null
                    && (player.Hand[bestIndex] == null || player.Hand[j].Cost > player.Hand[bestIndex].Cost))
                {
                    bestIndex = j;
                }
            }
            CardStateSnapshot card = player.Hand[bestIndex];
            player.Hand.RemoveAt(bestIndex);
            card.ResetRuntimeState(CardState.Graveyard);
            player.Graveyard.Add(card);
        }
    }

    public static void AddStats(CardStateSnapshot card, int attack, int health)
    {
        if (card == null)
        {
            return;
        }

        card.Attack += attack;
        card.MaxHealth += health;
        card.Health = Math.Min(card.MaxHealth, Math.Max(0, card.Health + health));
    }

    public static void HealCard(CardStateSnapshot card, int amount)
    {
        if (card != null && amount > 0)
        {
            card.Health = Math.Min(card.MaxHealth, card.Health + amount);
        }
    }

    public static void HealPlayer(PlayerStateSnapshot player, int amount)
    {
        if (player != null && amount > 0)
        {
            player.Health = Math.Min(player.MaxHealth, player.Health + amount);
        }
    }

    public static void ReturnToHand(BattleStateSnapshot state, CardStateSnapshot card)
    {
        if (card == null || state == null)
        {
            return;
        }

        PlayerStateSnapshot owner = state.GetPlayer(card.OwnerIndex);
        if (owner == null)
        {
            return;
        }

        owner.Field.Remove(card);
        owner.Graveyard.Remove(card);
        if (owner.Hand.Count >= GameConst.handMax)
        {
            card.ResetRuntimeState(CardState.Graveyard);
            if (!owner.Graveyard.Contains(card))
            {
                owner.Graveyard.Add(card);
            }

            return;
        }

        if (!owner.Hand.Contains(card))
        {
            owner.Hand.Add(card);
        }

        card.ResetRuntimeState(CardState.Hand);
    }

    public static void Revive(PlayerStateSnapshot owner, CardStateSnapshot card)
    {
        if (owner == null
            || card == null
            || owner.Field.Count >= GameConst.fieldMax
            || !owner.Graveyard.Contains(card)
            || card.Data == null
            || card.Data.cardType != CardType.Minion)
        {
            return;
        }

        owner.Graveyard.Remove(card);
        card.ResetRuntimeState(CardState.Field);
        owner.Field.Add(card);
    }
}
