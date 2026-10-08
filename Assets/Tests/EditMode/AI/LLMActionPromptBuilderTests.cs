using System.Collections.Generic;
using NUnit.Framework;

public class LLMActionPromptBuilderTests
{
    [Test]
    public void BuildPrompt_SplitsMinionAndSpellPlayActions()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot minion = SimulationTestHelpers.CreateCard(11, 0, CardState.Hand, 2, 3, PassiveType.Guard, false);
        minion.Data.name = "近卫";
        minion.Data.cost = 3;
        minion.Cost = 3;
        minion.Data.effectDescription = "进场获得嘲讽";
        minion.Data.effects.Add(new CardEffectData
        {
            triggerType = TriggerType.Enter,
            effectType = EffectType.BuffSelf,
            effectValues = new[] { 1, 1 },
        });
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(12, 0, new CardEffectData
        {
            effectType = EffectType.DealDamageToEnemy,
            effectValues = new[] { 6, 1 },
        });
        spell.Data.name = "火球";
        spell.Data.cost = 4;
        spell.Cost = 4;
        spell.Data.effectDescription = "造成6点伤害";
        state.GetPlayer(0).Hand.Add(minion);
        state.GetPlayer(0).Hand.Add(spell);

        LLMStrategyPrompt prompt = LLMActionPromptBuilder.BuildPrompt(state, new List<SimulatedAction>
        {
            new() { Type = SimulatedActionType.PlayHandCard, SourceCardId = minion.RuntimeId },
            new() { Type = SimulatedActionType.PlayHandCard, SourceCardId = spell.RuntimeId },
        });

        Assert.AreEqual("PlayMinion", prompt.legal_actions[0].type);
        Assert.AreEqual("Minion", prompt.legal_actions[0].card_type);
        Assert.AreEqual("近卫", prompt.legal_actions[0].card_name);
        Assert.AreEqual(3, prompt.legal_actions[0].cost);
        Assert.AreEqual(2, prompt.legal_actions[0].attack);
        Assert.AreEqual(3, prompt.legal_actions[0].health);
        CollectionAssert.AreEqual(new[] { "Guard" }, prompt.legal_actions[0].passives);
        CollectionAssert.AreEqual(new[] { "BuffSelf" }, prompt.legal_actions[0].effect_types);
        Assert.AreEqual("进场获得嘲讽", prompt.legal_actions[0].effect_description);

        Assert.AreEqual("PlaySpell", prompt.legal_actions[1].type);
        Assert.AreEqual("SPELL", prompt.legal_actions[1].card_type);
        Assert.AreEqual("火球", prompt.legal_actions[1].card_name);
        Assert.AreEqual(4, prompt.legal_actions[1].cost);
        Assert.AreEqual(0, prompt.legal_actions[1].attack);
        Assert.AreEqual(0, prompt.legal_actions[1].health);
        Assert.AreEqual(0, prompt.legal_actions[1].passives.Length);
        CollectionAssert.AreEqual(new[] { "DealDamageToEnemy" }, prompt.legal_actions[1].effect_types);
        Assert.AreEqual("造成6点伤害", prompt.legal_actions[1].effect_description);
    }

    [Test]
    public void BuildAction_DescribesCardAndHeroTargets()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Health = 18;
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(30, 0, new CardEffectData
        {
            effectType = EffectType.DealDamageToEnemy,
            effectValues = new[] { 3, 1 },
        });
        spell.Data.name = "点射";
        CardStateSnapshot enemy = SimulationTestHelpers.CreateCard(40, 1, CardState.Field, 5, 2, PassiveType.None, false);
        enemy.Data.name = "高攻怪";
        state.GetPlayer(0).Hand.Add(spell);
        state.GetPlayer(1).Field.Add(enemy);

        LLMActionOption minionTarget = LLMActionPromptBuilder.BuildAction(state, new SimulatedAction
        {
            Type = SimulatedActionType.PlayHandCard,
            SourceCardId = spell.RuntimeId,
            Targets = new List<SimulatedTarget> { SimulatedTarget.Card(enemy.RuntimeId) },
        }, 0);
        Assert.AreEqual(1, minionTarget.targets.Length);
        Assert.AreEqual(enemy.RuntimeId, minionTarget.targets[0].id);
        Assert.AreEqual("Card", minionTarget.targets[0].kind);
        Assert.AreEqual("高攻怪", minionTarget.targets[0].name);
        Assert.AreEqual("enemy", minionTarget.targets[0].owner);
        Assert.AreEqual(5, minionTarget.targets[0].attack);
        Assert.AreEqual(2, minionTarget.targets[0].health);

        CardStateSnapshot attacker = SimulationTestHelpers.CreateCard(50, 0, CardState.Field, 3, 2, PassiveType.Charge, true);
        attacker.Data.name = "先锋";
        state.GetPlayer(0).Field.Add(attacker);
        LLMActionOption heroTarget = LLMActionPromptBuilder.BuildAction(state, new SimulatedAction
        {
            Type = SimulatedActionType.AttackPlayer,
            SourceCardId = attacker.RuntimeId,
            Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) },
        }, 1);
        Assert.AreEqual("AttackPlayer", heroTarget.type);
        Assert.AreEqual(1, heroTarget.targets.Length);
        Assert.AreEqual("Player", heroTarget.targets[0].kind);
        Assert.AreEqual("敌方英雄", heroTarget.targets[0].name);
        Assert.AreEqual("enemy", heroTarget.targets[0].owner);
        Assert.AreEqual(0, heroTarget.targets[0].attack);
        Assert.AreEqual(18, heroTarget.targets[0].health);
        CollectionAssert.AreEqual(new[] { "Charge" }, heroTarget.passives);
        Assert.AreEqual(0, heroTarget.effect_types.Length);
    }

    [Test]
    public void BuildAction_EndTurnHasEmptySourceFields()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        LLMActionOption option = LLMActionPromptBuilder.BuildAction(
            state,
            new SimulatedAction { Type = SimulatedActionType.EndTurn },
            2);

        Assert.AreEqual("EndTurn", option.type);
        Assert.AreEqual(0, option.source_card_id);
        Assert.AreEqual(string.Empty, option.card_type);
        Assert.AreEqual(string.Empty, option.card_name);
        Assert.AreEqual(0, option.cost);
        Assert.AreEqual(0, option.attack);
        Assert.AreEqual(0, option.health);
        Assert.AreEqual(0, option.passives.Length);
        Assert.AreEqual(0, option.effect_types.Length);
        Assert.AreEqual(string.Empty, option.effect_description);
        Assert.AreEqual(0, option.targets.Length);
    }

    [Test]
    public void BuildAction_MissingCardDataFallsBackToPlayHandCard()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        LLMActionOption option = LLMActionPromptBuilder.BuildAction(
            state,
            new SimulatedAction { Type = SimulatedActionType.PlayHandCard, SourceCardId = 999 },
            0);

        Assert.AreEqual("PlayHandCard", option.type);
        Assert.AreEqual(999, option.source_card_id);
        Assert.AreEqual(string.Empty, option.card_type);
        Assert.AreEqual(string.Empty, option.card_name);
    }

    [Test]
    public void FormatAction_IncludesCardNameAndTargetSummary()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Health = 18;
        CardStateSnapshot attacker = SimulationTestHelpers.CreateCard(50, 0, CardState.Field, 3, 2, PassiveType.Charge, true);
        attacker.Data.name = "先锋";
        state.GetPlayer(0).Field.Add(attacker);
        string formatted = LLMActionPromptBuilder.FormatAction(state, new SimulatedAction
        {
            Type = SimulatedActionType.AttackPlayer,
            SourceCardId = attacker.RuntimeId,
            Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) },
        });

        StringAssert.Contains("AttackPlayer", formatted);
        StringAssert.Contains("先锋", formatted);
        StringAssert.Contains("敌方英雄", formatted);
        StringAssert.Contains("Charge", formatted);
    }
}
