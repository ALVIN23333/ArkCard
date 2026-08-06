using System.Collections.Generic;
using NUnit.Framework;

internal static class SimulationTestHelpers
{
    public static BattleStateSnapshot CreateBaseState()
    {
        return new BattleStateSnapshot
        {
            CurrentPlayerIndex = 0,
            Players = new List<PlayerStateSnapshot>
            {
                new() { PlayerIndex = 0, Health = 30, MaxHealth = 30, Cost = 10, MaxCost = 10 },
                new() { PlayerIndex = 1, Health = 30, MaxHealth = 30, Cost = 10, MaxCost = 10 },
            },
        };
    }

    public static CardStateSnapshot CreateCard(
        int id,
        int owner,
        CardState state,
        int attack,
        int health,
        PassiveType passive,
        bool canAttack,
        CardType cardType = CardType.Minion)
    {
        return CreateCard(id, owner, state, attack, health, new List<PassiveType> { passive }, canAttack, cardType);
    }

    public static CardStateSnapshot CreateCard(
        int id,
        int owner,
        CardState state,
        int attack,
        int health,
        List<PassiveType> passives,
        bool canAttack,
        CardType cardType = CardType.Minion)
    {
        CardData data = new()
        {
            index = id,
            name = $"Test {id}",
            cardType = cardType,
            cost = 0,
            attack = attack,
            health = health,
            passiveTypes = passives,
            effects = new List<CardEffectData>(),
        };
        return new CardStateSnapshot
        {
            RuntimeId = id,
            OwnerIndex = owner,
            State = state,
            Data = data,
            Cost = data.cost,
            Attack = attack,
            Health = health,
            MaxHealth = health,
            CanAttack = canAttack,
            CanAttackPlayer = canAttack,
            AttacksRemaining = passives != null && passives.Contains(PassiveType.Windfury) ? 2 : 1,
            IsStealth = passives != null && passives.Contains(PassiveType.Stealth),
            HolyShield = passives != null && passives.Contains(PassiveType.HolyShield) ? 1 : 0,
        };
    }

    public static CardStateSnapshot CreateSpell(int id, int owner, params CardEffectData[] effects)
    {
        CardStateSnapshot spell = CreateCard(id, owner, CardState.Hand, 0, 0, PassiveType.None, false, CardType.SPELL);
        spell.Data.effects.AddRange(effects);
        return spell;
    }

    public static SimulatedAction FindPlaySpellAction(BattleStateSnapshot state, int spellRuntimeId)
    {
        return new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.PlayHandCard && candidate.SourceCardId == spellRuntimeId);
    }

    public static BattleStateSnapshot PlaySpell(BattleStateSnapshot state, int spellRuntimeId, int seed = 11)
    {
        SimulatedAction action = FindPlaySpellAction(state, spellRuntimeId);
        Assert.IsNotNull(action, $"No playable action for spell {spellRuntimeId}");
        return new BattleStateSimulator().ApplyAction(state, action, new System.Random(seed));
    }
}
