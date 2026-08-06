using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 从 AIPlannerSelfTest 迁移而来的 AI 规划层回归测试（原 Tools/ArkCards/Run AI v1 Self Tests）。
/// </summary>
public class AIPlannerMigrationTests
{
    [Test]
    public void GuardRules_BlockPlayerAttack_ButAllowMinionAttack()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(1, 0, CardState.Field, 4, 5, PassiveType.None, true));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(2, 1, CardState.Field, 2, 6, PassiveType.Guard, false));

        List<SimulatedAction> actions = new BattleStateSimulator().GenerateLegalActions(state);

        Assert.IsFalse(actions.Exists(action => action.Type == SimulatedActionType.AttackPlayer), "Guard must block player attacks.");
        Assert.IsTrue(
            actions.Exists(action => action.Type == SimulatedActionType.AttackMinion && action.Targets[0].Id == 2),
            "Guard must remain attackable.");
    }

    [Test]
    public void Mcts_SelectsImmediateLethal()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Health = 5;
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 6, 4, PassiveType.None, true));

        MCTSResult result = new MCTSPlanner(new MCTSSettings
        {
            Iterations = 200,
            TimeBudgetMs = 1000,
            ExplorationConstant = 1.4,
            RolloutActionLimit = 4,
            ExpandTopCandidatesBias = 3,
        }, 1234).Search(state);

        Assert.IsNotNull(result.SelectedAction, "MCTS must return a selected action.");
        Assert.AreEqual(SimulatedActionType.AttackPlayer, result.SelectedAction.Type, "MCTS must select immediate lethal.");
        Assert.AreEqual(200, result.CompletedIterations, "Iteration budget must be honored when time permits.");
    }

    [Test]
    public void TargetSelection_PrefersExactKill()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateCard(20, 0, CardState.Hand, 0, 0, PassiveType.None, false, CardType.SPELL);
        CardEffectData effect = new() { effectType = EffectType.DealDamageToEnemy, effectValues = new[] { 3, 1 } };
        source.Data.effects.Add(effect);
        state.GetPlayer(0).Hand.Add(source);
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(21, 1, CardState.Field, 5, 3, PassiveType.None, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(22, 1, CardState.Field, 2, 8, PassiveType.None, false));

        List<SimulatedTarget> selected = AITargetSelector.SelectTargets(state, source, effect, 1);

        Assert.AreEqual(1, selected.Count, "Exactly one target must be selected.");
        Assert.AreEqual(SimulatedTargetKind.Card, selected[0].Kind, "Target must be a card.");
        Assert.AreEqual(21, selected[0].Id, "Damage targeting must prefer a valuable exact kill.");
    }

    [Test]
    public void Snapshot_IsolationAndTopDeckDraw()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(30, 0,
            new CardEffectData { effectType = EffectType.Draw, effectValues = new[] { 1 } });
        state.GetPlayer(0).Hand.Add(spell);
        state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(31, 0, CardState.Deck, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(32, 0, CardState.Deck, 2, 2, PassiveType.None, false));

        SimulatedAction action = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.PlayHandCard && candidate.SourceCardId == spell.RuntimeId);
        Assert.IsNotNull(action, "Draw spell must be playable.");

        BattleStateSimulator simulator = new();
        BattleStateSnapshot result = simulator.ApplyAction(state, action, new System.Random(7));

        Assert.AreEqual(2, state.GetPlayer(0).DeckRemaining.Count, "Simulation must not mutate the root snapshot.");
        Assert.AreEqual(1, state.GetPlayer(0).Hand.Count, "Simulation must not mutate the root snapshot.");
        Assert.AreEqual(1, result.GetPlayer(0).DeckRemaining.Count, "Draw must consume the top deck card.");
        Assert.AreEqual(1, result.GetPlayer(0).Hand.Count, "Draw must add one card to hand.");
        Assert.AreEqual(31, result.GetPlayer(0).Hand[0].RuntimeId, "Draw must take the top deck card.");

        for (int seed = 0; seed < 5; seed++)
        {
            BattleStateSnapshot sample = simulator.ApplyAction(state, action, new System.Random(seed));
            Assert.AreEqual(31, sample.GetPlayer(0).Hand[0].RuntimeId, "Draw result must not depend on the seed once deck order is fixed.");
        }
    }

    [Test]
    public void AllEffectTypes_ExecuteWithoutCorruptingState()
    {
        foreach (EffectType effectType in System.Enum.GetValues(typeof(EffectType)))
        {
            if (effectType == EffectType.None)
            {
                continue;
            }

            BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
            state.GetPlayer(0).Cost = 10;
            state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(100, 0, CardState.Field, 2, 4, PassiveType.None, true));
            state.GetPlayer(0).Graveyard.Add(SimulationTestHelpers.CreateCard(101, 0, CardState.Graveyard, 3, 3, PassiveType.Rush, false));
            state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(102, 0, CardState.Deck, 1, 1, PassiveType.None, false));
            state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(103, 1, CardState.Field, 3, 3, PassiveType.Guard, false));
            CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(200 + (int)effectType, 0,
                new CardEffectData { effectType = effectType, effectValues = new[] { 2, 1, 1 } });
            state.GetPlayer(0).Hand.Add(spell);

            SimulatedAction action = new BattleStateSimulator().GenerateLegalActions(state)
                .Find(candidate => candidate.Type == SimulatedActionType.PlayHandCard && candidate.SourceCardId == spell.RuntimeId);
            Assert.IsNotNull(action, $"Effect {effectType} must produce a playable action.");

            BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, action, new System.Random(11));
            Assert.IsNotNull(result.FindCard(spell.RuntimeId), $"Effect {effectType} must complete without corrupting card state.");
        }
    }

    [Test]
    public void SingleAction_BypassesSearch()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        MCTSResult result = new MCTSPlanner(new MCTSSettings(), 1).Search(state);
        Assert.IsTrue(result.SkippedSearch, "A sole action must bypass MCTS.");
        Assert.IsNotNull(result.SelectedAction, "Sole action must be returned as selection.");
        Assert.AreEqual(SimulatedActionType.EndTurn, result.SelectedAction.Type, "A sole EndTurn action must bypass MCTS.");
    }
}
