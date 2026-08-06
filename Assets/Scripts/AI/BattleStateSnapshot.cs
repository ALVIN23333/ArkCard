using System;
using System.Collections.Generic;
using System.Text;

public enum SimulatedActionType
{
    PlayHandCard,
    UseFieldCast,
    AttackMinion,
    AttackPlayer,
    EndTurn,
}

public enum SimulatedTargetKind
{
    Card,
    Player,
}

[Serializable]
public sealed class SimulatedTarget : IEquatable<SimulatedTarget>
{
    public SimulatedTargetKind Kind;
    public int Id;

    public static SimulatedTarget Card(int runtimeId)
    {
        return new SimulatedTarget { Kind = SimulatedTargetKind.Card, Id = runtimeId };
    }

    public static SimulatedTarget Player(int playerIndex)
    {
        return new SimulatedTarget { Kind = SimulatedTargetKind.Player, Id = playerIndex };
    }

    public bool Equals(SimulatedTarget other)
    {
        return other != null && Kind == other.Kind && Id == other.Id;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as SimulatedTarget);
    }

    public override int GetHashCode()
    {
        return ((int)Kind * 397) ^ Id;
    }

    public override string ToString()
    {
        return Kind == SimulatedTargetKind.Player ? $"Player[{Id}]" : $"Card[{Id}]";
    }
}

[Serializable]
public sealed class SimulatedAction : IEquatable<SimulatedAction>
{
    public SimulatedActionType Type;
    public int SourceCardId;
    public List<SimulatedTarget> Targets = new();
    public double PriorHeuristic;

    public bool Equals(SimulatedAction other)
    {
        if (other == null || Type != other.Type || SourceCardId != other.SourceCardId || Targets.Count != other.Targets.Count)
        {
            return false;
        }

        for (int i = 0; i < Targets.Count; i++)
        {
            if (!Targets[i].Equals(other.Targets[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as SimulatedAction);
    }

    public override int GetHashCode()
    {
        int hash = ((int)Type * 397) ^ SourceCardId;
        foreach (SimulatedTarget target in Targets)
        {
            hash = (hash * 397) ^ target.GetHashCode();
        }
        return hash;
    }

    public override string ToString()
    {
        string source = SourceCardId != 0 ? $" source={SourceCardId}" : string.Empty;
        string targets = Targets.Count > 0 ? $" targets={string.Join(",", Targets)}" : string.Empty;
        return $"{Type}{source}{targets}";
    }
}

[Serializable]
public sealed class CardStateSnapshot
{
    public int RuntimeId;
    public int OwnerIndex;
    public CardState State;
    public CardData Data;
    public int Cost;
    public int Attack;
    public int Health;
    public int MaxHealth;
    public bool CanAttack;
    public bool CanAttackPlayer;
    public int AttacksRemaining;
    public int HolyShield;
    public bool IsStealth;
    public bool CastUsed;
    public bool IsSilence;
    public bool IsDying;

    public bool HasPassive(PassiveType passive)
    {
        if (IsSilence || Data == null || Data.passiveTypes == null)
        {
            return false;
        }

        return Data.passiveTypes.Contains(passive);
    }

    public bool HasAnyPassive(params PassiveType[] passives)
    {
        foreach (PassiveType passive in passives)
        {
            if (HasPassive(passive))
            {
                return true;
            }
        }

        return false;
    }

    public CardStateSnapshot Clone()
    {
        return (CardStateSnapshot)MemberwiseClone();
    }

    public void ResetRuntimeState(CardState state)
    {
        State = state;
        Cost = Data != null ? Data.cost : 0;
        Attack = Data != null ? Data.attack : 0;
        Health = Data != null ? Data.health : 0;
        MaxHealth = Data != null ? Data.health : 0;
        CastUsed = false;
        IsSilence = false;
        IsDying = false;
        CanAttack = HasAnyPassive(PassiveType.Rush, PassiveType.Charge);
        CanAttackPlayer = HasPassive(PassiveType.Charge);
        AttacksRemaining = HasPassive(PassiveType.Windfury) ? 2 : 1;
        IsStealth = HasPassive(PassiveType.Stealth);
        HolyShield = HasPassive(PassiveType.HolyShield) ? 1 : 0;
    }
}

[Serializable]
public sealed class PlayerStateSnapshot
{
    public int PlayerIndex;
    public bool IsMainPlayer;
    public int Health;
    public int MaxHealth;
    public int Cost;
    public int MaxCost;
    public List<CardStateSnapshot> Hand = new();
    public List<CardStateSnapshot> Field = new();
    public List<CardStateSnapshot> Graveyard = new();
    public List<CardStateSnapshot> DeckRemaining = new();
    public bool HandIsHidden;
    public bool HiddenInformationMaterialized;
    public int HiddenHandCount;
    public int HiddenDeckCount;
    public List<CardData> HiddenCardPool = new();

    public PlayerStateSnapshot Clone()
    {
        PlayerStateSnapshot clone = (PlayerStateSnapshot)MemberwiseClone();
        clone.Hand = CloneCards(Hand);
        clone.Field = CloneCards(Field);
        clone.Graveyard = CloneCards(Graveyard);
        clone.DeckRemaining = CloneCards(DeckRemaining);
        clone.HiddenCardPool = HiddenCardPool != null ? new List<CardData>(HiddenCardPool) : new List<CardData>();
        return clone;
    }

    private static List<CardStateSnapshot> CloneCards(List<CardStateSnapshot> source)
    {
        List<CardStateSnapshot> result = new(source.Count);
        foreach (CardStateSnapshot card in source)
        {
            if (card != null)
            {
                result.Add(card.Clone());
            }
        }
        return result;
    }
}

[Serializable]
public sealed class BattleStateSnapshot
{
    public int CurrentPlayerIndex;
    public bool IsTurnEnded;
    public bool IsGameOver;
    public int RootPlayerIndex = -1;
    public int RootEndTurnCount;
    public int MaxRootTurns = 2;
    public List<PlayerStateSnapshot> Players = new();

    public BattleStateSnapshot Clone()
    {
        BattleStateSnapshot clone = (BattleStateSnapshot)MemberwiseClone();
        clone.Players = new List<PlayerStateSnapshot>(Players.Count);
        foreach (PlayerStateSnapshot player in Players)
        {
            if (player != null)
            {
                clone.Players.Add(player.Clone());
            }
        }
        return clone;
    }

    public PlayerStateSnapshot GetPlayer(int playerIndex)
    {
        foreach (PlayerStateSnapshot player in Players)
        {
            if (player != null && player.PlayerIndex == playerIndex)
            {
                return player;
            }
        }
        return null;
    }

    public CardStateSnapshot FindCard(int runtimeId)
    {
        foreach (PlayerStateSnapshot player in Players)
        {
            CardStateSnapshot card = FindCard(player.Hand, runtimeId)
                ?? FindCard(player.Field, runtimeId)
                ?? FindCard(player.Graveyard, runtimeId)
                ?? FindCard(player.DeckRemaining, runtimeId);
            if (card != null)
            {
                return card;
            }
        }
        return null;
    }

    public void Determinize(Random random)
    {
        random ??= new Random();
        int nextSyntheticId = -1;
        foreach (PlayerStateSnapshot player in Players)
        {
            if (player == null)
            {
                continue;
            }

            if (player.HandIsHidden)
            {
                List<CardData> pool = new(player.HiddenCardPool);
                for (int i = 0; i < player.HiddenHandCount && pool.Count > 0; i++)
                {
                    int index = random.Next(pool.Count);
                    player.Hand.Add(CreateHiddenCard(pool[index], player.PlayerIndex, ref nextSyntheticId, CardState.Hand));
                    pool.RemoveAt(index);
                }

                if (player.HiddenDeckCount > 0)
                {
                    player.DeckRemaining.Clear();
                    MaterializeDeck(player, pool, random, ref nextSyntheticId);
                }
                player.HiddenInformationMaterialized = true;
            }
            else if (player.HiddenDeckCount > 0 && player.DeckRemaining.Count > 0)
            {
                Shuffle(player.DeckRemaining, random);
            }
            else if (player.HiddenDeckCount > 0 && player.HiddenCardPool.Count > 0)
            {
                MaterializeDeck(player, new List<CardData>(player.HiddenCardPool), random, ref nextSyntheticId);
            }
        }
    }

    private static void MaterializeDeck(
        PlayerStateSnapshot player,
        List<CardData> pool,
        Random random,
        ref int nextSyntheticId)
    {
        int remaining = player.HiddenDeckCount;
        while (remaining > 0 && pool.Count > 0)
        {
            int index = random.Next(pool.Count);
            player.DeckRemaining.Add(CreateHiddenCard(pool[index], player.PlayerIndex, ref nextSyntheticId, CardState.Deck));
            pool.RemoveAt(index);
            remaining--;
        }

        // Pool exhausted: sample with replacement from the original pool so large decks stay playable.
        while (remaining > 0 && player.HiddenCardPool.Count > 0)
        {
            int index = random.Next(player.HiddenCardPool.Count);
            player.DeckRemaining.Add(CreateHiddenCard(player.HiddenCardPool[index], player.PlayerIndex, ref nextSyntheticId, CardState.Deck));
            remaining--;
        }

        Shuffle(player.DeckRemaining, random);
    }

    private static CardStateSnapshot CreateHiddenCard(CardData data, int ownerIndex, ref int nextSyntheticId, CardState state)
    {
        CardStateSnapshot card = new()
        {
            RuntimeId = nextSyntheticId,
            OwnerIndex = ownerIndex,
            Data = data,
        };
        nextSyntheticId--;
        card.ResetRuntimeState(state);
        return card;
    }

    private static void Shuffle(List<CardStateSnapshot> cards, Random random)
    {
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            CardStateSnapshot temporary = cards[i];
            cards[i] = cards[j];
            cards[j] = temporary;
        }
    }

    public string GetSummary()
    {
        StringBuilder builder = new();
        builder.Append("current=").Append(CurrentPlayerIndex)
            .Append(", ended=").Append(IsTurnEnded)
            .Append(", gameOver=").Append(IsGameOver);
        foreach (PlayerStateSnapshot player in Players)
        {
            builder.Append(" | P").Append(player.PlayerIndex)
                .Append(" hp=").Append(player.Health)
                .Append(" cost=").Append(player.Cost).Append('/').Append(player.MaxCost)
                .Append(" hand=").Append(player.HandIsHidden ? player.HiddenHandCount : player.Hand.Count)
                .Append(" field=").Append(player.Field.Count)
                .Append(" grave=").Append(player.Graveyard.Count)
                .Append(" deck=").Append(player.HiddenDeckCount > 0 ? player.HiddenDeckCount : player.DeckRemaining.Count);
        }
        return builder.ToString();
    }

    private static CardStateSnapshot FindCard(List<CardStateSnapshot> cards, int runtimeId)
    {
        foreach (CardStateSnapshot card in cards)
        {
            if (card != null && card.RuntimeId == runtimeId)
            {
                return card;
            }
        }
        return null;
    }
}
