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
            int randomIndex = (random ??= new Random()).Next(player.Hand.Count);
            CardStateSnapshot card = player.Hand[randomIndex];
            player.Hand.RemoveAt(randomIndex);
            if (card == null)
            {
                i--;
                continue;
            }
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

    public static void ReturnToHand(BattleStateSnapshot state, CardStateSnapshot card, Random random)
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

        owner.Graveyard.Remove(card);
        if (owner.Hand.Count >= GameConst.handMax)
        {
            // Hand is full: the returning minion is destroyed instead, triggering its deathrattle.
            random ??= new Random();
            EffectSimulationResolver.KillCard(state, card, random);
            return;
        }

        owner.Field.Remove(card);
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

    public static void ReviveForController(BattleStateSnapshot state, PlayerStateSnapshot destination, CardStateSnapshot card)
    {
        if (state == null || destination == null || card == null || destination.Field.Count >= GameConst.fieldMax
            || card.Data == null || card.Data.cardType != CardType.Minion)
        {
            return;
        }

        PlayerStateSnapshot previousOwner = state.GetPlayer(card.OwnerIndex);
        if (previousOwner == null || !previousOwner.Graveyard.Remove(card))
        {
            return;
        }

        card.OwnerIndex = destination.PlayerIndex;
        card.ResetRuntimeState(CardState.Field);
        destination.Field.Add(card);
    }

    public static void Summon(BattleStateSnapshot state, PlayerStateSnapshot owner, CardData data, int count)
    {
        if (state == null || owner == null || data == null || data.cardType != CardType.Minion || count <= 0)
        {
            return;
        }

        int nextId = -1;
        foreach (PlayerStateSnapshot player in state.Players)
        {
            foreach (CardStateSnapshot card in player.Hand) nextId = Math.Min(nextId, card.RuntimeId - 1);
            foreach (CardStateSnapshot card in player.Field) nextId = Math.Min(nextId, card.RuntimeId - 1);
            foreach (CardStateSnapshot card in player.Graveyard) nextId = Math.Min(nextId, card.RuntimeId - 1);
            foreach (CardStateSnapshot card in player.DeckRemaining) nextId = Math.Min(nextId, card.RuntimeId - 1);
        }

        int summonCount = Math.Min(count, Math.Max(0, GameConst.fieldMax - owner.Field.Count));
        for (int i = 0; i < summonCount; i++)
        {
            CardStateSnapshot card = new() { RuntimeId = nextId--, OwnerIndex = owner.PlayerIndex, Data = data };
            card.ResetRuntimeState(CardState.Field);
            owner.Field.Add(card);
        }
    }
}
