using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 对 BattleStateSimulator + EffectSimulationResolver 的精确数值断言，
/// 覆盖全部 EffectType 与关键被动/条件分支。
/// </summary>
public class EffectValueAssertionTests
{
    [Test]
    public void Draw_MovesExactCardFromDeckToHand()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(50, 0,
            new CardEffectData { effectType = EffectType.Draw, effectValues = new[] { 1 } });
        state.GetPlayer(0).Hand.Add(spell);
        state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(51, 0, CardState.Deck, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(52, 0, CardState.Deck, 2, 2, PassiveType.None, false));

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(1, result.GetPlayer(0).Hand.Count, "Draw must add exactly one card to hand.");
        Assert.AreEqual(1, result.GetPlayer(0).DeckRemaining.Count, "Draw must remove exactly one card from deck.");
    }

    [Test]
    public void AddCostMax_IncreasesMaxCostAndCapsAtCostMax()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).MaxCost = 5;
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(60, 0,
            new CardEffectData { effectType = EffectType.AddCostMax, effectValues = new[] { 2 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);
        Assert.AreEqual(7, result.GetPlayer(0).MaxCost, "AddCostMax must add exactly the given amount.");

        BattleStateSnapshot capped = SimulationTestHelpers.CreateBaseState();
        capped.GetPlayer(0).MaxCost = 9;
        CardStateSnapshot capSpell = SimulationTestHelpers.CreateSpell(61, 0,
            new CardEffectData { effectType = EffectType.AddCostMax, effectValues = new[] { 3 } });
        capped.GetPlayer(0).Hand.Add(capSpell);

        BattleStateSnapshot cappedResult = SimulationTestHelpers.PlaySpell(capped, capSpell.RuntimeId);
        Assert.AreEqual(GameConst.costMax, cappedResult.GetPlayer(0).MaxCost, "AddCostMax must cap at costMax.");
    }

    [Test]
    public void AddCost_AddsExactCost()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Cost = 2;
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(62, 0,
            new CardEffectData { effectType = EffectType.AddCost, effectValues = new[] { 3 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);
        Assert.AreEqual(5, result.GetPlayer(0).Cost, "AddCost must add exactly the given amount.");
    }

    [Test]
    public void AddBothCost_AddsExactCostAndMaxCost()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Cost = 1;
        state.GetPlayer(0).MaxCost = 3;
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(63, 0,
            new CardEffectData { effectType = EffectType.AddBothCost, effectValues = new[] { 2 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);
        Assert.AreEqual(3, result.GetPlayer(0).Cost, "AddBothCost must add exact cost.");
        Assert.AreEqual(5, result.GetPlayer(0).MaxCost, "AddBothCost must add exact max cost.");
    }

    [Test]
    public void DisCard_DiscardsExactCountFromHand()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(64, 0,
            new CardEffectData { effectType = EffectType.DisCard, effectValues = new[] { 2 } });
        state.GetPlayer(0).Hand.Add(spell);
        state.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(65, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(66, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(67, 0, CardState.Hand, 1, 1, PassiveType.None, false));

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(1, result.GetPlayer(0).Hand.Count, "DisCard must discard exactly two cards (spell already removed).");
        Assert.AreEqual(3, result.GetPlayer(0).Graveyard.Count, "Graveyard must contain the spell plus two discarded cards.");
    }

    [Test]
    public void BuffSelf_AddsExactStatsViaFieldCast()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot minion = SimulationTestHelpers.CreateCard(70, 0, CardState.Field, 3, 4, PassiveType.None, false);
        minion.Data.effects.Add(new CardEffectData
        {
            triggerType = TriggerType.Cast,
            effectType = EffectType.BuffSelf,
            effectValues = new[] { 2, 1 },
        });
        state.GetPlayer(0).Field.Add(minion);

        SimulatedAction action = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.UseFieldCast && candidate.SourceCardId == minion.RuntimeId);
        Assert.IsNotNull(action, "Minion with Cast effect must be usable as field cast.");

        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, action, new System.Random(11));
        CardStateSnapshot buffed = result.FindCard(minion.RuntimeId);
        Assert.AreEqual(5, buffed.Attack, "BuffSelf must add exact attack.");
        Assert.AreEqual(5, buffed.Health, "BuffSelf must add exact health.");
        Assert.AreEqual(5, buffed.MaxHealth, "BuffSelf must increase max health.");
    }

    [Test]
    public void BuffAlliesAll_BuffsOnlyOwnerField()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(71, 0, CardState.Field, 3, 4, PassiveType.None, false));
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(72, 0, CardState.Field, 1, 2, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(73, 1, CardState.Field, 2, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(74, 0,
            new CardEffectData { effectType = EffectType.BuffAlliesAll, effectValues = new[] { 1, 1 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(4, result.FindCard(71).Attack);
        Assert.AreEqual(5, result.FindCard(71).Health);
        Assert.AreEqual(2, result.FindCard(72).Attack);
        Assert.AreEqual(3, result.FindCard(72).Health);
        Assert.AreEqual(2, result.FindCard(73).Attack, "Enemy field must not be buffed.");
        Assert.AreEqual(3, result.FindCard(73).Health, "Enemy field must not be buffed.");
    }

    [Test]
    public void BuffAllEnemies_BuffsOnlyEnemyField()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(75, 0, CardState.Field, 3, 4, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(76, 1, CardState.Field, 2, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(77, 0,
            new CardEffectData { effectType = EffectType.BuffAllEnemies, effectValues = new[] { 1, 1 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(3, result.FindCard(76).Attack, "Enemy minion must be buffed.");
        Assert.AreEqual(4, result.FindCard(76).Health, "Enemy minion must be buffed.");
        Assert.AreEqual(3, result.FindCard(75).Attack, "Ally minion must not be buffed.");
    }

    [Test]
    public void HealAlliesAll_HealsPlayerAndCardsCappedAtMax()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Health = 10;
        CardStateSnapshot damagedAlly = SimulationTestHelpers.CreateCard(78, 0, CardState.Field, 1, 4, PassiveType.None, false);
        damagedAlly.Health = 1;
        state.GetPlayer(0).Field.Add(damagedAlly);
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(79, 0,
            new CardEffectData { effectType = EffectType.healAlliesAll, effectValues = new[] { 2 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);
        Assert.AreEqual(12, result.GetPlayer(0).Health, "HealAlliesAll must heal the player.");
        Assert.AreEqual(3, result.FindCard(78).Health, "HealAlliesAll must heal the ally card.");

        BattleStateSnapshot capped = SimulationTestHelpers.CreateBaseState();
        capped.GetPlayer(0).Health = 29;
        capped.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(80, 0, CardState.Field, 1, 4, PassiveType.None, false));
        CardStateSnapshot capSpell = SimulationTestHelpers.CreateSpell(81, 0,
            new CardEffectData { effectType = EffectType.healAlliesAll, effectValues = new[] { 2 } });
        capped.GetPlayer(0).Hand.Add(capSpell);

        BattleStateSnapshot cappedResult = SimulationTestHelpers.PlaySpell(capped, capSpell.RuntimeId);
        Assert.AreEqual(30, cappedResult.GetPlayer(0).Health, "Heal must not exceed max health.");
        Assert.AreEqual(4, cappedResult.FindCard(80).Health, "Card heal must not exceed max health.");
    }

    [Test]
    public void BuffAlly_AppliesToSelectionCount()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(82, 0, CardState.Field, 3, 4, PassiveType.None, false));
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(83, 0, CardState.Field, 1, 2, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(84, 0,
            new CardEffectData { effectType = EffectType.BuffAlly, effectValues = new[] { 1, 1, 2 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(4, result.FindCard(82).Attack, "BuffAlly must buff first selected ally.");
        Assert.AreEqual(5, result.FindCard(82).Health);
        Assert.AreEqual(2, result.FindCard(83).Attack, "BuffAlly selection count must cover the second ally.");
        Assert.AreEqual(3, result.FindCard(83).Health);
    }

    [Test]
    public void BuffEnemy_AppliesToSelectionCount()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(85, 1, CardState.Field, 2, 3, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(86, 1, CardState.Field, 5, 5, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(87, 0,
            new CardEffectData { effectType = EffectType.BuffEnemy, effectValues = new[] { 1, 0, 2 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(3, result.FindCard(85).Attack, "BuffEnemy must buff first enemy.");
        Assert.AreEqual(6, result.FindCard(86).Attack, "BuffEnemy selection count must cover the second enemy.");
        Assert.AreEqual(3, result.FindCard(85).Health, "BuffEnemy must not change health when value is 0.");
    }

    [Test]
    public void HealAlly_HealsSelectedTargetCappedAtMax()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot damagedAlly = SimulationTestHelpers.CreateCard(88, 0, CardState.Field, 1, 4, PassiveType.None, false);
        damagedAlly.Health = 1;
        state.GetPlayer(0).Field.Add(damagedAlly);
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(89, 0,
            new CardEffectData { effectType = EffectType.HealAlly, effectValues = new[] { 2 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);
        Assert.AreEqual(3, result.FindCard(88).Health, "HealAlly must heal the selected ally.");
    }

    [Test]
    public void DealDamageToEnemy_KillsExactHealthTarget()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(90, 1, CardState.Field, 3, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(91, 0,
            new CardEffectData { effectType = EffectType.DealDamageToEnemy, effectValues = new[] { 3 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(30, result.GetPlayer(1).Health, "Player must not be damaged when a minion is killed.");
        Assert.AreEqual(0, result.GetPlayer(1).Field.Count, "Exact-health minion must be killed.");
        Assert.AreEqual(CardState.Graveyard, result.FindCard(90).State, "Killed minion must move to graveyard.");
    }

    [Test]
    public void DealDamageToEnemy_DamagesPlayerWhenNoField()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(92, 0,
            new CardEffectData { effectType = EffectType.DealDamageToEnemy, effectValues = new[] { 3 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);
        Assert.AreEqual(27, result.GetPlayer(1).Health, "Damage must hit the enemy player when the field is empty.");
    }

    [Test]
    public void DamageAll_DamagesEveryone()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(93, 0, CardState.Field, 3, 4, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(94, 1, CardState.Field, 2, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(95, 0,
            new CardEffectData { effectType = EffectType.DamageAll, effectValues = new[] { 2 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(28, result.GetPlayer(0).Health);
        Assert.AreEqual(28, result.GetPlayer(1).Health);
        Assert.AreEqual(2, result.FindCard(93).Health, "Ally minion must take damage.");
        Assert.AreEqual(1, result.FindCard(94).Health, "Enemy minion must take damage.");
    }

    [Test]
    public void DamageAllEnemy_DamagesEnemyOnly()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(96, 0, CardState.Field, 3, 4, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(97, 1, CardState.Field, 2, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(98, 0,
            new CardEffectData { effectType = EffectType.DamageAllEnemy, effectValues = new[] { 2 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(30, result.GetPlayer(0).Health, "Ally player must not take damage.");
        Assert.AreEqual(4, result.FindCard(96).Health, "Ally minion must not take damage.");
        Assert.AreEqual(28, result.GetPlayer(1).Health, "Enemy player must take damage.");
        Assert.AreEqual(1, result.FindCard(97).Health, "Enemy minion must take damage.");
    }

    [Test]
    public void DamageAll_TriggersGameOverWhenAllPlayersDie()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(99, 0,
            new CardEffectData { effectType = EffectType.DamageAll, effectValues = new[] { 30 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);
        Assert.AreEqual(0, result.GetPlayer(0).Health);
        Assert.AreEqual(0, result.GetPlayer(1).Health);
        Assert.IsTrue(result.IsGameOver, "Game must be over when both players die.");
    }

    [Test]
    public void SlienceEnemy_DisablesGuardPassive()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(200, 0, CardState.Field, 4, 5, PassiveType.None, true));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(201, 1, CardState.Field, 2, 6, PassiveType.Guard, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(202, 0,
            new CardEffectData { effectType = EffectType.SlienceEnemy, effectValues = new[] { 1 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.IsTrue(result.FindCard(201).IsSilence, "Guard minion must be silenced.");
        Assert.IsFalse(
            result.FindCard(201).HasPassive(PassiveType.Guard),
            "Silenced minion must lose its guard passive.");
        Assert.IsTrue(
            new BattleStateSimulator().GenerateLegalActions(result)
                .Exists(action => action.Type == SimulatedActionType.AttackPlayer),
            "Silencing the guard must re-enable player attacks.");
    }

    [Test]
    public void DestoryEnemy_MovesTargetToGraveyard()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(203, 1, CardState.Field, 2, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(204, 0,
            new CardEffectData { effectType = EffectType.DestoryEnemy, effectValues = new[] { 1 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(0, result.GetPlayer(1).Field.Count, "Destroyed minion must leave the field.");
        Assert.AreEqual(CardState.Graveyard, result.FindCard(203).State);
    }

    [Test]
    public void AllyBackHand_ReturnsAllyMinionToHand()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(205, 0, CardState.Field, 3, 4, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(206, 0,
            new CardEffectData { effectType = EffectType.AllyBackHand, effectValues = new[] { 1 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(0, result.GetPlayer(0).Field.Count, "Returned minion must leave the field.");
        Assert.AreEqual(1, result.GetPlayer(0).Hand.Count, "Returned minion must enter the hand.");
        Assert.AreEqual(CardState.Hand, result.FindCard(205).State);
    }

    [Test]
    public void EnemyBackHand_ReturnsEnemyMinionToEnemyHand()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(207, 1, CardState.Field, 2, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(208, 0,
            new CardEffectData { effectType = EffectType.EnemyBackHand, effectValues = new[] { 1 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(0, result.GetPlayer(1).Field.Count);
        Assert.AreEqual(1, result.GetPlayer(1).Hand.Count, "Enemy minion must return to the enemy hand.");
    }

    [Test]
    public void OtherBackHand_PrefersHigherThreatOtherAlly()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(209, 0, CardState.Field, 3, 4, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(210, 1, CardState.Field, 2, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(211, 0,
            new CardEffectData { effectType = EffectType.OtherBackHand, effectValues = new[] { 1 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(1, result.GetPlayer(0).Hand.Count, "Higher-threat ally must be returned to hand.");
        Assert.AreEqual(CardState.Hand, result.FindCard(209).State);
        Assert.AreEqual(CardState.Field, result.FindCard(210).State, "Enemy minion must not be returned.");
    }

    [Test]
    public void ReviveAlly_RevivesGraveyardMinionToField()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(212, 0, CardState.Field, 3, 4, PassiveType.None, false));
        state.GetPlayer(0).Graveyard.Add(SimulationTestHelpers.CreateCard(213, 0, CardState.Graveyard, 3, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(214, 0,
            new CardEffectData { effectType = EffectType.ReviveAlly, effectValues = new[] { 1 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(2, result.GetPlayer(0).Field.Count, "Revived minion must enter the field.");
        Assert.AreEqual(CardState.Field, result.FindCard(213).State);
        Assert.AreEqual(CardState.Graveyard, result.FindCard(214).State, "Played spell must move to the graveyard.");
        Assert.IsFalse(
            result.GetPlayer(0).Graveyard.Contains(result.FindCard(213)),
            "Revived minion must leave the graveyard.");
    }

    [Test]
    public void ReviveAlly_DoesNothingWhenFieldIsFull()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        for (int i = 0; i < GameConst.fieldMax; i++)
        {
            state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(220 + i, 0, CardState.Field, 1, 1, PassiveType.None, false));
        }
        state.GetPlayer(0).Graveyard.Add(SimulationTestHelpers.CreateCard(230, 0, CardState.Graveyard, 3, 3, PassiveType.None, false));
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(231, 0,
            new CardEffectData { effectType = EffectType.ReviveAlly, effectValues = new[] { 1 } });
        state.GetPlayer(0).Hand.Add(spell);

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(GameConst.fieldMax, result.GetPlayer(0).Field.Count, "Full field must block revival.");
        Assert.AreEqual(CardState.Graveyard, result.FindCard(230).State, "Graveyard minion must remain when field is full.");
        Assert.AreEqual(CardState.Graveyard, result.FindCard(231).State, "Played spell must move to the graveyard.");
    }

    [Test]
    public void Lifesteal_HealsOwnerForDamageDealt()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Health = 20;
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(300, 0, CardState.Field, 3, 2, PassiveType.Lifesteal, true));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(301, 1, CardState.Field, 2, 3, PassiveType.None, false));

        SimulatedAction action = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.AttackMinion && candidate.SourceCardId == 300);
        Assert.IsNotNull(action, "Lifesteal minion must be able to attack.");

        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, action, new System.Random(11));

        Assert.AreEqual(23, result.GetPlayer(0).Health, "Lifesteal must heal the owner by damage dealt.");
        Assert.AreEqual(CardState.Graveyard, result.FindCard(301).State, "Enemy minion must die from the attack.");
        Assert.AreEqual(CardState.Graveyard, result.FindCard(300).State, "Attacker must die from the trade.");
    }

    [Test]
    public void Poisonous_KillsMinionOnAnyDamage()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(302, 0, CardState.Field, 1, 4, PassiveType.Poisonous, true));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(303, 1, CardState.Field, 3, 3, PassiveType.None, false));

        SimulatedAction action = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.AttackMinion && candidate.SourceCardId == 302);
        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, action, new System.Random(11));

        Assert.AreEqual(CardState.Graveyard, result.FindCard(303).State, "Poisonous must destroy the target on any damage.");
        Assert.AreEqual(1, result.FindCard(302).Health, "Poisonous attacker must survive the trade.");
    }

    [Test]
    public void HolyShield_AbsorbsFirstDamage()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(304, 0, CardState.Field, 3, 4, PassiveType.None, true));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(305, 1, CardState.Field, 2, 3, PassiveType.HolyShield, false));

        SimulatedAction action = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.AttackMinion && candidate.SourceCardId == 304);
        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, action, new System.Random(11));

        Assert.AreEqual(3, result.FindCard(305).Health, "Holy shield must absorb all damage.");
        Assert.AreEqual(0, result.FindCard(305).HolyShield, "Holy shield must be consumed.");
        Assert.AreEqual(2, result.FindCard(304).Health, "Attacker must take counter damage.");
    }

    [Test]
    public void Swingle_DamagesAdjacentMinions()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(306, 0, CardState.Field, 3, 4, PassiveType.Swingle, true));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(307, 1, CardState.Field, 2, 2, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(308, 1, CardState.Field, 2, 2, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(309, 1, CardState.Field, 2, 2, PassiveType.None, false));

        SimulatedAction action = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.AttackMinion && candidate.SourceCardId == 306 && candidate.Targets[0].Id == 308);
        Assert.IsNotNull(action, "Swingle minion must be able to attack the middle minion.");

        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, action, new System.Random(11));

        Assert.AreEqual(0, result.GetPlayer(1).Field.Count, "Swingle must clear the target and both adjacent minions.");
        Assert.AreEqual(2, result.FindCard(306).Health, "Swingle attacker must take counter damage from the target.");
    }

    [Test]
    public void Windfury_AllowsSecondAttackThenExhausts()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(310, 0, CardState.Field, 3, 4, PassiveType.Windfury, true));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(311, 1, CardState.Field, 1, 2, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(312, 1, CardState.Field, 1, 2, PassiveType.None, false));

        SimulatedAction first = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.AttackMinion && candidate.SourceCardId == 310 && candidate.Targets[0].Id == 311);
        BattleStateSnapshot afterFirst = new BattleStateSimulator().ApplyAction(state, first, new System.Random(11));
        Assert.AreEqual(1, afterFirst.FindCard(310).AttacksRemaining, "Windfury must allow a second attack.");
        Assert.IsTrue(afterFirst.FindCard(310).CanAttack, "Windfury attacker must remain able to attack.");

        SimulatedAction second = new BattleStateSimulator().GenerateLegalActions(afterFirst)
            .Find(candidate => candidate.Type == SimulatedActionType.AttackMinion && candidate.SourceCardId == 310 && candidate.Targets[0].Id == 312);
        Assert.IsNotNull(second, "Second windfury attack must be legal.");
        BattleStateSnapshot afterSecond = new BattleStateSimulator().ApplyAction(afterFirst, second, new System.Random(11));
        Assert.AreEqual(0, afterSecond.FindCard(310).AttacksRemaining, "Windfury must be exhausted after two attacks.");
        Assert.IsFalse(afterSecond.FindCard(310).CanAttack, "Exhausted attacker must not be able to attack again.");
    }

    [Test]
    public void Stealth_ClearedAfterAttacking()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(313, 0, CardState.Field, 2, 3, PassiveType.Stealth, true));

        SimulatedAction action = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.AttackPlayer && candidate.SourceCardId == 313);
        Assert.IsNotNull(action, "Stealth attacker must be able to attack the player.");
        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, action, new System.Random(11));

        Assert.IsFalse(result.FindCard(313).IsStealth, "Stealth must be lost after attacking.");
    }

    [Test]
    public void Stealth_TargetsAreExcludedFromEnemySelection()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateCard(314, 0, CardState.Hand, 0, 0, PassiveType.None, false, CardType.SPELL);
        CardEffectData effect = new() { effectType = EffectType.DealDamageToEnemy, effectValues = new[] { 3 } };
        source.Data.effects.Add(effect);
        state.GetPlayer(0).Hand.Add(source);
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(315, 1, CardState.Field, 2, 3, PassiveType.Stealth, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(316, 1, CardState.Field, 5, 5, PassiveType.None, false));

        List<SimulatedTarget> candidates = AITargetSelector.GetCandidates(state, source, effect);

        Assert.IsFalse(candidates.Exists(target => target.Kind == SimulatedTargetKind.Card && target.Id == 315),
            "Stealth minion must not be selectable as a target.");
        Assert.IsTrue(candidates.Exists(target => target.Kind == SimulatedTargetKind.Card && target.Id == 316),
            "Visible minion must remain selectable.");
    }

    [Test]
    public void GuardWithStealth_DoesNotBlockPlayerAttacks()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(317, 0, CardState.Field, 4, 5, PassiveType.None, true));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(318, 1, CardState.Field, 2, 6, new List<PassiveType> { PassiveType.Guard, PassiveType.Stealth }, false));

        List<SimulatedAction> actions = new BattleStateSimulator().GenerateLegalActions(state);

        Assert.IsTrue(actions.Exists(action => action.Type == SimulatedActionType.AttackPlayer),
            "Stealth guard must not block player attacks.");
        Assert.IsFalse(
            actions.Exists(action => action.Type == SimulatedActionType.AttackMinion && action.Targets[0].Id == 318),
            "Stealth minion must not be attackable.");
    }

    [Test]
    public void ResetRuntimeState_DerivesAttackCapabilitiesFromPassives()
    {
        CardData rushData = new()
        {
            index = 1,
            name = "Rush",
            cardType = CardType.Minion,
            attack = 3,
            health = 4,
            passiveTypes = new List<PassiveType> { PassiveType.Rush },
        };
        CardStateSnapshot rush = new()
        {
            RuntimeId = 1,
            OwnerIndex = 0,
            State = CardState.Deck,
            Data = rushData,
            Cost = 0,
            Attack = 3,
            Health = 4,
            MaxHealth = 4,
        };
        rush.ResetRuntimeState(CardState.Field);
        Assert.IsTrue(rush.CanAttack, "Rush must allow attacking minions immediately.");
        Assert.IsFalse(rush.CanAttackPlayer, "Rush must not allow attacking the player immediately.");

        CardData chargeData = new()
        {
            index = 2,
            name = "Charge",
            cardType = CardType.Minion,
            attack = 3,
            health = 4,
            passiveTypes = new List<PassiveType> { PassiveType.Charge, PassiveType.Windfury, PassiveType.Stealth, PassiveType.HolyShield },
        };
        CardStateSnapshot charge = new()
        {
            RuntimeId = 2,
            OwnerIndex = 0,
            State = CardState.Deck,
            Data = chargeData,
            Cost = 0,
            Attack = 3,
            Health = 4,
            MaxHealth = 4,
        };
        charge.ResetRuntimeState(CardState.Field);
        Assert.IsTrue(charge.CanAttack, "Charge must allow attacking minions immediately.");
        Assert.IsTrue(charge.CanAttackPlayer, "Charge must allow attacking the player immediately.");
        Assert.AreEqual(2, charge.AttacksRemaining, "Windfury must grant two attacks.");
        Assert.IsTrue(charge.IsStealth, "Stealth passive must be applied on entry.");
        Assert.AreEqual(1, charge.HolyShield, "Holy shield must be applied on entry.");
    }

    [Test]
    public void ConditionalBranch_DrawsThenWhenConditionMet()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardEffectData conditional = new CardEffectData
        {
            conditionTypes = new List<ConditionType> { ConditionType.ThreeMoreHand },
            thenEffects = new List<CardEffectData>
            {
                new() { effectType = EffectType.Draw, effectValues = new[] { 1 } },
            },
            elseEffects = new List<CardEffectData>
            {
                new() { effectType = EffectType.Draw, effectValues = new[] { 2 } },
            },
        };
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(400, 0, conditional);
        state.GetPlayer(0).Hand.Add(spell);
        state.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(401, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(402, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(408, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(403, 0, CardState.Deck, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(404, 0, CardState.Deck, 1, 1, PassiveType.None, false));

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(4, result.GetPlayer(0).Hand.Count, "Then branch must draw exactly one card.");
        Assert.AreEqual(1, result.GetPlayer(0).DeckRemaining.Count, "Then branch must consume exactly one card from the deck.");
    }

    [Test]
    public void ConditionalBranch_DrawsElseWhenConditionNotMet()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardEffectData conditional = new CardEffectData
        {
            conditionTypes = new List<ConditionType> { ConditionType.ThreeMoreHand },
            thenEffects = new List<CardEffectData>
            {
                new() { effectType = EffectType.Draw, effectValues = new[] { 1 } },
            },
            elseEffects = new List<CardEffectData>
            {
                new() { effectType = EffectType.Draw, effectValues = new[] { 2 } },
            },
        };
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(405, 0, conditional);
        state.GetPlayer(0).Hand.Add(spell);
        state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(406, 0, CardState.Deck, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(407, 0, CardState.Deck, 1, 1, PassiveType.None, false));

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(2, result.GetPlayer(0).Hand.Count, "Else branch must draw exactly two cards.");
        Assert.AreEqual(0, result.GetPlayer(0).DeckRemaining.Count, "Else branch must consume exactly two cards from the deck.");
    }
}
