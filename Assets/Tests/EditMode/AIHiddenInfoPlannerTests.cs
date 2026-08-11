using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 隐藏信息确定性化、2-ply 回合转换与 CardData AI 配置接线测试。
/// </summary>
public class AIHiddenInfoPlannerTests
{
    // ---------- Determinize ----------

    [Test]
    public void Determinize_SamplesHiddenHandAndDeckFromPool()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        PlayerStateSnapshot opponent = state.GetPlayer(1);
        opponent.HandIsHidden = true;
        opponent.HiddenHandCount = 2;
        opponent.HiddenDeckCount = 3;
        opponent.HiddenCardPool = MakePool(1001, 1002, 1003, 1004, 1005);

        state.Determinize(new Random(7));

        Assert.AreEqual(2, opponent.Hand.Count, "Hidden hand must be sampled.");
        Assert.AreEqual(3, opponent.DeckRemaining.Count, "Remaining pool must materialize the hidden deck.");
        Assert.AreEqual(5, opponent.Hand.Count + opponent.DeckRemaining.Count, "Hand and deck must come from the same pool without replacement.");

        HashSet<int> ids = new();
        foreach (CardStateSnapshot card in opponent.Hand) ids.Add(card.RuntimeId);
        foreach (CardStateSnapshot card in opponent.DeckRemaining) ids.Add(card.RuntimeId);
        Assert.AreEqual(5, ids.Count, "Materialized cards must have unique synthetic ids.");
        foreach (int id in ids)
        {
            Assert.Less(id, 0, "Synthetic ids must be negative.");
        }

        List<int> dataIds = new();
        foreach (CardStateSnapshot card in opponent.Hand) dataIds.Add(card.Data.index);
        foreach (CardStateSnapshot card in opponent.DeckRemaining) dataIds.Add(card.Data.index);
        dataIds.Sort();
        CollectionAssert.AreEqual(new List<int> { 1001, 1002, 1003, 1004, 1005 }, dataIds, "Pool multiset must be preserved.");
    }

    [Test]
    public void Determinize_ShufflesAiOwnDeck_KeepingRuntimeIds()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        PlayerStateSnapshot ai = state.GetPlayer(0);
        ai.HiddenDeckCount = 4;
        ai.DeckRemaining.Add(SimulationTestHelpers.CreateCard(101, 0, CardState.Deck, 1, 1, PassiveType.None, false));
        ai.DeckRemaining.Add(SimulationTestHelpers.CreateCard(102, 0, CardState.Deck, 1, 1, PassiveType.None, false));
        ai.DeckRemaining.Add(SimulationTestHelpers.CreateCard(103, 0, CardState.Deck, 1, 1, PassiveType.None, false));
        ai.DeckRemaining.Add(SimulationTestHelpers.CreateCard(104, 0, CardState.Deck, 1, 1, PassiveType.None, false));

        state.Determinize(new Random(3));

        Assert.AreEqual(4, ai.DeckRemaining.Count);
        HashSet<int> runtimeIds = new();
        foreach (CardStateSnapshot card in ai.DeckRemaining) runtimeIds.Add(card.RuntimeId);
        Assert.AreEqual(4, runtimeIds.Count, "AI deck must keep real runtime ids (shuffle only).");
        Assert.IsFalse(runtimeIds.Contains(-1), "AI deck must not be rebuilt with synthetic ids.");
    }

    [Test]
    public void Determinize_DoesNotTouchDirectlyConstructedState()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(10, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        state.GetPlayer(0).DeckRemaining.Add(SimulationTestHelpers.CreateCard(11, 0, CardState.Deck, 1, 1, PassiveType.None, false));

        state.Determinize(new Random(5));

        Assert.AreEqual(1, state.GetPlayer(0).Hand.Count);
        Assert.AreEqual(10, state.GetPlayer(0).Hand[0].RuntimeId);
        Assert.AreEqual(1, state.GetPlayer(0).DeckRemaining.Count);
        Assert.AreEqual(11, state.GetPlayer(0).DeckRemaining[0].RuntimeId);
    }

    [Test]
    public void Determinize_DifferentSeedsProduceDifferentWorlds()
    {
        BattleStateSnapshot template = SimulationTestHelpers.CreateBaseState();
        PlayerStateSnapshot opponent = template.GetPlayer(1);
        opponent.HandIsHidden = true;
        opponent.HiddenHandCount = 3;
        opponent.HiddenDeckCount = 2;
        opponent.HiddenCardPool = MakePool(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        HashSet<string> signatures = new();
        for (int seed = 0; seed < 20; seed++)
        {
            BattleStateSnapshot state = template.Clone();
            state.Determinize(new Random(seed));
            List<int> handCardIndices = new();
            foreach (CardStateSnapshot card in state.GetPlayer(1).Hand) handCardIndices.Add(card.Data.index);
            handCardIndices.Sort();
            signatures.Add(string.Join(",", handCardIndices));
        }
        Assert.Greater(signatures.Count, 1, "Different seeds must sample different hidden worlds.");
    }

    [Test]
    public void Determinize_FallsBackToReplacementWhenPoolTooSmall()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        PlayerStateSnapshot opponent = state.GetPlayer(1);
        opponent.HandIsHidden = true;
        opponent.HiddenHandCount = 2;
        opponent.HiddenDeckCount = 5;
        opponent.HiddenCardPool = MakePool(1001, 1002);

        state.Determinize(new Random(9));

        Assert.AreEqual(2, opponent.Hand.Count);
        Assert.AreEqual(5, opponent.DeckRemaining.Count, "Deck larger than the pool must still materialize via replacement.");
    }

    // ---------- 2-ply turn transition ----------

    [Test]
    public void EndTurn_StartsOpponentTurn()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.RootPlayerIndex = 0;
        state.GetPlayer(0).Cost = 3;
        state.GetPlayer(0).MaxCost = 3;
        state.GetPlayer(1).Cost = 2;
        state.GetPlayer(1).MaxCost = 2;

        CardStateSnapshot startBuff = SimulationTestHelpers.CreateCard(50, 1, CardState.Field, 1, 1, PassiveType.None, false);
        startBuff.Data.effects.Add(new CardEffectData { triggerType = TriggerType.Start, effectType = EffectType.BuffSelf, effectValues = new[] { 1, 1 } });
        state.GetPlayer(1).Field.Add(startBuff);
        state.GetPlayer(1).DeckRemaining.Add(SimulationTestHelpers.CreateCard(60, 1, CardState.Deck, 2, 3, PassiveType.None, false));

        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, new SimulatedAction { Type = SimulatedActionType.EndTurn }, new Random(1));

        Assert.AreEqual(1, result.CurrentPlayerIndex, "Turn must switch to the opponent.");
        Assert.AreEqual(3, result.GetPlayer(1).MaxCost, "Opponent max cost must increment.");
        Assert.AreEqual(3, result.GetPlayer(1).Cost, "Opponent cost must refresh to max cost.");
        Assert.IsFalse(result.IsTurnEnded, "Search must continue during the opponent turn.");
        Assert.AreEqual(1, result.GetPlayer(1).Hand.Count, "Turn-start draw must take the top deck card.");
        Assert.AreEqual(60, result.GetPlayer(1).Hand[0].RuntimeId);
        Assert.AreEqual(2, result.GetPlayer(1).Field[0].Attack, "Start-of-turn effect must apply +1/+1.");
        Assert.AreEqual(2, result.GetPlayer(1).Field[0].Health);
        Assert.IsTrue(result.GetPlayer(1).Field[0].CanAttack, "Field attacks must refresh at turn start.");
    }

    [Test]
    public void EndTurn_ContinuesIntoRootSecondTurn_ThenStopsAfterSecondRootEnd()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.RootPlayerIndex = 0;
        state.MaxRootTurns = 2;
        BattleStateSimulator simulator = new();

        BattleStateSnapshot afterAi1 = simulator.ApplyAction(state, new SimulatedAction { Type = SimulatedActionType.EndTurn }, new Random(1));
        Assert.AreEqual(1, afterAi1.CurrentPlayerIndex);
        Assert.IsFalse(afterAi1.IsTurnEnded);
        Assert.AreEqual(1, afterAi1.RootEndTurnCount);

        BattleStateSnapshot afterOpponent = simulator.ApplyAction(afterAi1, new SimulatedAction { Type = SimulatedActionType.EndTurn }, new Random(1));
        Assert.AreEqual(0, afterOpponent.CurrentPlayerIndex, "Opponent EndTurn must return to the root player (AI second turn).");
        Assert.IsFalse(afterOpponent.IsTurnEnded, "Search must continue into the root player's second turn.");

        BattleStateSnapshot afterAi2 = simulator.ApplyAction(afterOpponent, new SimulatedAction { Type = SimulatedActionType.EndTurn }, new Random(1));
        Assert.AreEqual(0, afterAi2.CurrentPlayerIndex, "Search must stop before switching away from the root player.");
        Assert.IsTrue(afterAi2.IsTurnEnded, "Search must stop after the root player's second EndTurn.");
        Assert.AreEqual(2, afterAi2.RootEndTurnCount);
    }

    [Test]
    public void EndTurn_StopsAfterFirstRootTurn_WhenMaxRootTurnsIsOne()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.RootPlayerIndex = 0;
        state.MaxRootTurns = 1;
        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, new SimulatedAction { Type = SimulatedActionType.EndTurn }, new Random(1));
        Assert.AreEqual(0, result.CurrentPlayerIndex);
        Assert.IsTrue(result.IsTurnEnded);
    }

    [Test]
    public void EndTurn_LegacyBehavior_WithoutRootPlayerIndex()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, new SimulatedAction { Type = SimulatedActionType.EndTurn }, new Random(1));
        Assert.AreEqual(0, result.CurrentPlayerIndex, "Legacy EndTurn must not switch players.");
        Assert.IsTrue(result.IsTurnEnded, "Legacy EndTurn must end the search.");
    }

    // ---------- 2-ply decision quality ----------

    [Test]
    public void Mcts_PrefersTauntOverEndTurn_WhenOpponentHasChargeLethal()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.CurrentPlayerIndex = 0;
        state.GetPlayer(0).Health = 5;
        state.GetPlayer(0).Cost = 10;
        state.GetPlayer(0).MaxCost = 10;

        CardStateSnapshot taunt = SimulationTestHelpers.CreateCard(10, 0, CardState.Hand, 2, 3, PassiveType.Guard, false);
        taunt.Data.cost = 2;
        taunt.Cost = 2;
        state.GetPlayer(0).Hand.Add(taunt);
        // A non-attacking minion so ending the turn without playing the taunt is a plausible branch.
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(11, 0, CardState.Field, 3, 2, PassiveType.None, false));

        CardStateSnapshot charger = SimulationTestHelpers.CreateCard(20, 1, CardState.Hand, 6, 2, PassiveType.Charge, true);
        charger.Data.cost = 4;
        charger.Cost = 4;
        state.GetPlayer(1).Health = 30;
        state.GetPlayer(1).Cost = 10;
        state.GetPlayer(1).MaxCost = 10;
        state.GetPlayer(1).Hand.Add(charger);

        MCTSResult result = new MCTSPlanner(new MCTSSettings
        {
            Iterations = 500,
            TimeBudgetMs = 3000,
            ExplorationConstant = 1.4,
            RolloutActionLimit = 6,
            ExpandTopCandidatesBias = 3,
        }, 42).Search(state);

        Assert.IsNotNull(result.SelectedAction, "MCTS must return a selected action.");
        Assert.AreNotEqual(SimulatedActionType.EndTurn, result.SelectedAction.Type, "AI must not end turn into an immediate charge lethal.");
        Assert.AreEqual(SimulatedActionType.PlayHandCard, result.SelectedAction.Type, "AI should play the taunt to block lethal.");
        Assert.AreEqual(10, result.SelectedAction.SourceCardId);
    }

    // ---------- CardData AI config wiring ----------

    [Test]
    public void AggressiveStyle_ScoresFaceHigherThanDefensive()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot attacker = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 5, 4, PassiveType.None, true);
        attacker.Data.aiPlayStyle = AIPlayStyle.Aggressive;
        CardStateSnapshot defensive = SimulationTestHelpers.CreateCard(11, 0, CardState.Field, 5, 4, PassiveType.None, true);
        defensive.Data.aiPlayStyle = AIPlayStyle.Defensive;
        state.GetPlayer(0).Field.Add(attacker);
        state.GetPlayer(0).Field.Add(defensive);
        SimulatedAction faceAggressive = new() { Type = SimulatedActionType.AttackPlayer, SourceCardId = 10, Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) } };
        SimulatedAction faceDefensive = new() { Type = SimulatedActionType.AttackPlayer, SourceCardId = 11, Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) } };

        double aggressiveScore = HeuristicEvaluator.ScoreAction(state, faceAggressive);
        double defensiveScore = HeuristicEvaluator.ScoreAction(state, faceDefensive);

        Assert.Greater(aggressiveScore, defensiveScore, "Aggressive style must prefer face damage over Defensive.");
    }

    [Test]
    public void DefensiveStyle_ScoresTradeHigherThanAggressive()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot attacker = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 5, 4, PassiveType.None, true);
        attacker.Data.aiPlayStyle = AIPlayStyle.Aggressive;
        CardStateSnapshot defensive = SimulationTestHelpers.CreateCard(11, 0, CardState.Field, 5, 4, PassiveType.None, true);
        defensive.Data.aiPlayStyle = AIPlayStyle.Defensive;
        state.GetPlayer(0).Field.Add(attacker);
        state.GetPlayer(0).Field.Add(defensive);
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 3, 3, PassiveType.None, false));
        SimulatedAction tradeAggressive = new() { Type = SimulatedActionType.AttackMinion, SourceCardId = 10, Targets = new List<SimulatedTarget> { SimulatedTarget.Card(20) } };
        SimulatedAction tradeDefensive = new() { Type = SimulatedActionType.AttackMinion, SourceCardId = 11, Targets = new List<SimulatedTarget> { SimulatedTarget.Card(20) } };

        double aggressiveScore = HeuristicEvaluator.ScoreAction(state, tradeAggressive);
        double defensiveScore = HeuristicEvaluator.ScoreAction(state, tradeDefensive);

        Assert.Greater(defensiveScore, aggressiveScore, "Defensive style must prefer minion trades over Aggressive.");
    }

    [Test]
    public void SupportAndValueRoles_GetRoleBonus()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot support = SimulationTestHelpers.CreateCard(10, 0, CardState.Hand, 2, 2, PassiveType.None, false);
        support.Data.aiRole = CardAIRole.Support;
        CardStateSnapshot value = SimulationTestHelpers.CreateCard(11, 0, CardState.Hand, 2, 2, PassiveType.None, false);
        value.Data.aiRole = CardAIRole.Value;
        CardStateSnapshot none = SimulationTestHelpers.CreateCard(12, 0, CardState.Hand, 2, 2, PassiveType.None, false);
        none.Data.aiRole = CardAIRole.None;
        state.GetPlayer(0).Cost = 10;
        state.GetPlayer(0).Hand.Add(support);
        state.GetPlayer(0).Hand.Add(value);
        state.GetPlayer(0).Hand.Add(none);

        double supportScore = HeuristicEvaluator.ScoreAction(state, Play(support));
        double valueScore = HeuristicEvaluator.ScoreAction(state, Play(value));
        double noneScore = HeuristicEvaluator.ScoreAction(state, Play(none));

        Assert.Greater(supportScore, noneScore, "Support role must receive a play bonus.");
        Assert.Greater(valueScore, noneScore, "Value role must receive a play bonus.");
    }

    [Test]
    public void LethalBonus_IncreasesFaceAttackPriority()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot attacker = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.None, true);
        attacker.Data.aiLethalBonus = 5;
        CardStateSnapshot plain = SimulationTestHelpers.CreateCard(11, 0, CardState.Field, 3, 3, PassiveType.None, true);
        plain.Data.aiLethalBonus = 0;
        state.GetPlayer(0).Field.Add(attacker);
        state.GetPlayer(0).Field.Add(plain);
        SimulatedAction faceBonus = new() { Type = SimulatedActionType.AttackPlayer, SourceCardId = 10, Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) } };
        SimulatedAction facePlain = new() { Type = SimulatedActionType.AttackPlayer, SourceCardId = 11, Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) } };

        Assert.Greater(HeuristicEvaluator.ScoreAction(state, faceBonus), HeuristicEvaluator.ScoreAction(state, facePlain));
    }

    // ---------- Lifesteal / draw overflow / exposure heuristics ----------

    [Test]
    public void AttackMinion_PrioritizesLifestealTarget()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot attacker = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 5, 4, PassiveType.None, true);
        state.GetPlayer(0).Field.Add(attacker);
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 3, PassiveType.Lifesteal, false));
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(21, 1, CardState.Field, 2, 3, PassiveType.None, false));
        SimulatedAction attackLifesteal = new() { Type = SimulatedActionType.AttackMinion, SourceCardId = 10, Targets = new List<SimulatedTarget> { SimulatedTarget.Card(20) } };
        SimulatedAction attackPlain = new() { Type = SimulatedActionType.AttackMinion, SourceCardId = 10, Targets = new List<SimulatedTarget> { SimulatedTarget.Card(21) } };

        Assert.Greater(
            HeuristicEvaluator.ScoreAction(state, attackLifesteal),
            HeuristicEvaluator.ScoreAction(state, attackPlain),
            "Lifesteal targets must be prioritized for removal.");
    }

    [Test]
    public void PoisonousMinion_AttackPlayerScoreZero_WhenEnemyMinionExists()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot poisonous = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.Poisonous, true);
        state.GetPlayer(0).Field.Add(poisonous);
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 2, PassiveType.None, false));
        SimulatedAction face = new() { Type = SimulatedActionType.AttackPlayer, SourceCardId = 10, Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) } };

        Assert.AreEqual(0, HeuristicEvaluator.ScoreAction(state, face), "Poisonous minion must not want to attack the hero while an enemy minion exists.");
    }

    [Test]
    public void PoisonousMinion_FaceActionRemainsLegal_WhenEnemyMinionExists()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot poisonous = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.Poisonous, true);
        state.GetPlayer(0).Field.Add(poisonous);
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 2, PassiveType.None, false));

        List<SimulatedAction> actions = new BattleStateSimulator().GenerateLegalActions(state);
        Assert.IsTrue(
            actions.Exists(action => action.Type == SimulatedActionType.AttackPlayer),
            "Legal action generation must not remove a valid hero attack because of a policy preference.");
    }

    [Test]
    public void PoisonousMinion_FaceActionAllowed_WhenEnemyFieldEmpty()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot poisonous = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.Poisonous, true);
        state.GetPlayer(0).Field.Add(poisonous);

        List<SimulatedAction> actions = new BattleStateSimulator().GenerateLegalActions(state);
        Assert.IsTrue(
            actions.Exists(action => action.Type == SimulatedActionType.AttackPlayer),
            "Poisonous minion must attack the hero when the enemy field is empty.");
    }

    [Test]
    public void PoisonousMinion_FaceActionAllowed_WhenLethal()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Health = 2;
        CardStateSnapshot poisonous = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.Poisonous, true);
        state.GetPlayer(0).Field.Add(poisonous);
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 2, PassiveType.None, false));

        List<SimulatedAction> actions = new BattleStateSimulator().GenerateLegalActions(state);
        Assert.IsTrue(
            actions.Exists(action => action.Type == SimulatedActionType.AttackPlayer),
            "Poisonous minion must go face when it kills the hero.");
    }

    [Test]
    public void PoisonousMinion_AttackPlayerAllowed_WhenEnemyFieldEmpty()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot poisonous = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.Poisonous, true);
        state.GetPlayer(0).Field.Add(poisonous);
        SimulatedAction face = new() { Type = SimulatedActionType.AttackPlayer, SourceCardId = 10, Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) } };

        Assert.Greater(HeuristicEvaluator.ScoreAction(state, face), 0, "Poisonous minion must attack the hero when the enemy field is empty.");
    }

    [Test]
    public void PoisonousMinion_AttackPlayerAllowed_WhenLethal()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Health = 2;
        CardStateSnapshot poisonous = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.Poisonous, true);
        state.GetPlayer(0).Field.Add(poisonous);
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 2, PassiveType.None, false));
        SimulatedAction face = new() { Type = SimulatedActionType.AttackPlayer, SourceCardId = 10, Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) } };

        Assert.GreaterOrEqual(HeuristicEvaluator.ScoreAction(state, face), 1000, "Poisonous minion must go face when it kills the hero.");
    }

    [Test]
    public void PoisonousMinion_AttackPlayerAllowed_WhenOnlyStealthEnemies()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot poisonous = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.Poisonous, true);
        state.GetPlayer(0).Field.Add(poisonous);
        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 2, PassiveType.Stealth, false));
        SimulatedAction face = new() { Type = SimulatedActionType.AttackPlayer, SourceCardId = 10, Targets = new List<SimulatedTarget> { SimulatedTarget.Player(1) } };

        Assert.Greater(HeuristicEvaluator.ScoreAction(state, face), 0, "Poisonous minion must attack the hero when all enemies are stealth.");
    }

    [Test]
    public void Evaluate_PenalizesEnemyLifesteal_WhenNoLethal()
    {
        BattleStateSnapshot noLethalLifesteal = SimulationTestHelpers.CreateBaseState();
        noLethalLifesteal.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.None, true));
        noLethalLifesteal.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 2, PassiveType.Lifesteal, false));

        BattleStateSnapshot noLethalPlain = SimulationTestHelpers.CreateBaseState();
        noLethalPlain.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.None, true));
        noLethalPlain.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 2, PassiveType.None, false));

        BattleStateSnapshot lethalLifesteal = SimulationTestHelpers.CreateBaseState();
        lethalLifesteal.GetPlayer(1).Health = 2;
        lethalLifesteal.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.None, true));
        lethalLifesteal.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 2, PassiveType.Lifesteal, false));

        BattleStateSnapshot lethalPlain = SimulationTestHelpers.CreateBaseState();
        lethalPlain.GetPlayer(1).Health = 2;
        lethalPlain.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 3, 3, PassiveType.None, true));
        lethalPlain.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 2, 2, PassiveType.None, false));

        double noLethalDelta = HeuristicEvaluator.Evaluate(noLethalLifesteal, 0) - HeuristicEvaluator.Evaluate(noLethalPlain, 0);
        double lethalDelta = HeuristicEvaluator.Evaluate(lethalLifesteal, 0) - HeuristicEvaluator.Evaluate(lethalPlain, 0);

        Assert.Less(noLethalDelta, lethalDelta, "The lifesteal penalty must be stronger when the root player cannot lethal.");
    }

    [Test]
    public void DrawCards_BurnOverflowWhenHandAtCap()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        PlayerStateSnapshot player = state.GetPlayer(0);
        for (int i = 0; i < GameConst.handMax - 1; i++)
        {
            player.Hand.Add(SimulationTestHelpers.CreateCard(100 + i, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        }
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(300, 0, new CardEffectData { effectType = EffectType.Draw, effectValues = new[] { 2 } });
        player.Hand.Add(spell);
        player.DeckRemaining.Add(SimulationTestHelpers.CreateCard(200, 0, CardState.Deck, 2, 2, PassiveType.None, false));
        player.DeckRemaining.Add(SimulationTestHelpers.CreateCard(201, 0, CardState.Deck, 3, 3, PassiveType.None, false));

        SimulatedAction action = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(candidate => candidate.Type == SimulatedActionType.PlayHandCard && candidate.SourceCardId == 300);
        Assert.IsNotNull(action, "Draw spell must be playable.");

        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, action, new Random(1));

        Assert.AreEqual(
            GameConst.handMax,
            result.GetPlayer(0).Hand.Count,
            "Hand must stay at the cap.");
        Assert.AreEqual(0, result.GetPlayer(0).DeckRemaining.Count);
        Assert.IsTrue(
            result.GetPlayer(0).Graveyard.Exists(card => card.RuntimeId == 201),
            "One overflow deck card must burn to the graveyard.");
        Assert.IsTrue(
            result.GetPlayer(0).Hand.Exists(card => card.RuntimeId == 200),
            "The first drawn card must enter the hand while the resolving spell no longer occupies a hand slot.");
        Assert.IsTrue(
            result.GetPlayer(0).Graveyard.Exists(card => card.RuntimeId == 300),
            "The played draw spell must end in the graveyard.");
    }

    [Test]
    public void DrawOne_FromFullHandWithPlayedSpell_EntersHand()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        PlayerStateSnapshot player = state.GetPlayer(0);
        for (int i = 0; i < GameConst.handMax - 1; i++)
        {
            player.Hand.Add(SimulationTestHelpers.CreateCard(400 + i, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        }
        CardStateSnapshot spell = SimulationTestHelpers.CreateSpell(410, 0, new CardEffectData { effectType = EffectType.Draw, effectValues = new[] { 1 } });
        player.Hand.Add(spell);
        player.DeckRemaining.Add(SimulationTestHelpers.CreateCard(411, 0, CardState.Deck, 2, 2, PassiveType.None, false));

        BattleStateSnapshot result = SimulationTestHelpers.PlaySpell(state, spell.RuntimeId);

        Assert.AreEqual(
            GameConst.handMax,
            result.GetPlayer(0).Hand.Count,
            "Drawn card must enter the hand since the resolving spell no longer occupies a hand slot.");
        Assert.AreEqual(0, result.GetPlayer(0).DeckRemaining.Count);
        Assert.IsTrue(
            result.GetPlayer(0).Hand.Exists(card => card.RuntimeId == 411),
            "Drawn card must enter the hand.");
        Assert.IsTrue(
            result.GetPlayer(0).Graveyard.Exists(card => card.RuntimeId == 410),
            "Played spell must end in the graveyard.");
    }

    [Test]
    public void DrawSpell_ScoreDropsNearHandCap()
    {
        BattleStateSnapshot lowHand = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot spellLow = SimulationTestHelpers.CreateSpell(10, 0, new CardEffectData { effectType = EffectType.Draw, effectValues = new[] { 1 } });
        lowHand.GetPlayer(0).Hand.Add(spellLow);
        for (int i = 0; i < 4; i++)
        {
            lowHand.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(100 + i, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        }
        lowHand.GetPlayer(0).Cost = 10;

        BattleStateSnapshot fullHand = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot spellFull = SimulationTestHelpers.CreateSpell(20, 0, new CardEffectData { effectType = EffectType.Draw, effectValues = new[] { 1 } });
        fullHand.GetPlayer(0).Hand.Add(spellFull);
        for (int i = 0; i < GameConst.handMax - 1; i++)
        {
            fullHand.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(200 + i, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        }
        fullHand.GetPlayer(0).Cost = 10;

        double lowScore = HeuristicEvaluator.ScoreAction(lowHand, Play(spellLow));
        double fullScore = HeuristicEvaluator.ScoreAction(fullHand, Play(spellFull));

        Assert.Greater(lowScore, fullScore, "Draw spells must score lower when the hand is at the cap.");
    }

    [Test]
    public void Evaluate_PenalizesFullHand()
    {
        BattleStateSnapshot full = SimulationTestHelpers.CreateBaseState();
        for (int i = 0; i < GameConst.handMax; i++)
        {
            full.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(100 + i, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        }

        BattleStateSnapshot nine = SimulationTestHelpers.CreateBaseState();
        for (int i = 0; i < GameConst.handMax - 1; i++)
        {
            nine.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(200 + i, 0, CardState.Hand, 1, 1, PassiveType.None, false));
        }

        Assert.Less(HeuristicEvaluator.Evaluate(full, 0), HeuristicEvaluator.Evaluate(nine, 0), "A full hand must be penalized below a near-full hand.");
    }

    [Test]
    public void Evaluate_PenalizesExposedBoardAfterAllInAttack()
    {
        BattleStateSnapshot exposed = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot attacked = SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 4, 2, PassiveType.None, true);
        attacked.CanAttack = false;
        attacked.AttacksRemaining = 0;
        exposed.GetPlayer(0).Field.Add(attacked);
        exposed.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 5, 5, PassiveType.None, true));

        BattleStateSnapshot safe = SimulationTestHelpers.CreateBaseState();
        safe.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 4, 2, PassiveType.None, true));
        safe.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(20, 1, CardState.Field, 5, 5, PassiveType.None, true));

        Assert.Less(
            HeuristicEvaluator.Evaluate(exposed, 0),
            HeuristicEvaluator.Evaluate(safe, 0),
            "An all-in-exposed board must be penalized below the same board that can still attack.");
    }

    [Test]
    public void PlannerDefaults_UseIncreasedSearchBudget()
    {
        MCTSSettings settings = new();
        settings.Clamp();
        Assert.AreEqual(400, settings.Iterations);
        Assert.AreEqual(50, settings.TimeBudgetMs);
        Assert.AreEqual(8, settings.RolloutActionLimit);
        Assert.AreEqual(2, settings.MaxRootTurns);
        Assert.AreEqual(10, settings.MaxActionsPerNode);
    }

    // ---------- Validation ----------

    [Test]
    public void Validation_FlagsComboReserveWithoutThreshold()
    {
        CardData card = CreateCardData();
        card.aiPlayStyle = AIPlayStyle.ComboReserve;
        card.aiComboReserveThreshold = 0;
        CardListSO database = new() { cards = new List<CardData> { card } };

        List<CardValidationMessage> messages = CardValidationService.Validate(database, 0);
        Assert.IsTrue(
            messages.Exists(message => message.Severity == CardValidationSeverity.Warning && message.PropertyPath.EndsWith("aiComboReserveThreshold")),
            "ComboReserve with threshold 0 must warn.");
    }

    [Test]
    public void Validation_FlagsThresholdAboveCostMax()
    {
        CardData card = CreateCardData();
        card.aiPlayStyle = AIPlayStyle.ComboReserve;
        card.aiComboReserveThreshold = GameConst.costMax + 1;
        CardListSO database = new() { cards = new List<CardData> { card } };

        List<CardValidationMessage> messages = CardValidationService.Validate(database, 0);
        Assert.IsTrue(
            messages.Exists(message => message.Severity == CardValidationSeverity.Warning && message.PropertyPath.EndsWith("aiComboReserveThreshold")),
            "Threshold above cost max must warn.");
    }

    private static SimulatedAction Play(CardStateSnapshot card)
    {
        return new SimulatedAction { Type = SimulatedActionType.PlayHandCard, SourceCardId = card.RuntimeId };
    }

    private static List<CardData> MakePool(params int[] indices)
    {
        List<CardData> pool = new();
        foreach (int index in indices)
        {
            pool.Add(new CardData
            {
                index = index,
                cardType = CardType.Minion,
                name = "Pool" + index,
                cost = 1,
                attack = 1,
                health = 1,
                effects = new List<CardEffectData>(),
                passiveTypes = new List<PassiveType>(),
            });
        }
        return pool;
    }

    private static CardData CreateCardData()
    {
        return new CardData
        {
            index = 1,
            cardType = CardType.Minion,
            name = "Test",
            cost = 1,
            attack = 1,
            health = 1,
            effects = new List<CardEffectData>(),
            passiveTypes = new List<PassiveType>(),
        };
    }
}
