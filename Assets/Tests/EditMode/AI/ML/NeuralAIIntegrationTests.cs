using System;
using System.Collections.Generic;
using NUnit.Framework;

public class NeuralAIIntegrationTests
{
    [Test]
    public void FeatureEncoder_MatchesDeclaredSchema()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        SimulatedAction action = new() { Type = SimulatedActionType.EndTurn };

        Assert.AreEqual(AIEncodingSchema.StateFeatureCount, AIFeatureEncoder.EncodeState(state, 0).Length);
        Assert.AreEqual(AIEncodingSchema.ActionFeatureCount, AIFeatureEncoder.EncodeAction(state, 0, action).Length);
        Assert.AreEqual(
            AIEncodingSchema.PolicyInputFeatureCount,
            AIFeatureEncoder.CombinePolicyInput(
                AIFeatureEncoder.EncodeState(state, 0),
                AIFeatureEncoder.EncodeAction(state, 0, action)).Length);
    }

    [Test]
    public void FeatureEncoder_DeterminizationDoesNotLeakOpponentPrivateCards()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        PlayerStateSnapshot opponent = state.GetPlayer(1);
        opponent.HandIsHidden = true;
        opponent.HiddenHandCount = 2;
        opponent.HiddenDeckCount = 2;
        opponent.HiddenCardPool.Add(CreateCardData(100, 1, 1));
        opponent.HiddenCardPool.Add(CreateCardData(101, 8, 8));
        opponent.HiddenCardPool.Add(CreateCardData(102, 2, 6));
        opponent.HiddenCardPool.Add(CreateCardData(103, 7, 2));

        BattleStateSnapshot first = state.Clone();
        BattleStateSnapshot second = state.Clone();
        first.Determinize(new Random(1));
        second.Determinize(new Random(9));

        CollectionAssert.AreEqual(
            AIFeatureEncoder.EncodeState(first, 0),
            AIFeatureEncoder.EncodeState(second, 0),
            "The root observer must see the same features for every hidden-card sample.");
        CollectionAssert.AreNotEqual(
            AIFeatureEncoder.EncodeState(first, 1),
            AIFeatureEncoder.EncodeState(second, 1),
            "The sampled opponent may see its own determinized private cards when acting.");
    }

    [Test]
    public void FeatureEncoder_DoesNotConsumeLegacyAIMetadata()
    {
        BattleStateSnapshot first = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot firstCard = SimulationTestHelpers.CreateCard(10, 0, CardState.Hand, 2, 3, PassiveType.Guard, false);
        first.GetPlayer(0).Hand.Add(firstCard);
        BattleStateSnapshot second = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot changedCard = SimulationTestHelpers.CreateCard(10, 0, CardState.Hand, 2, 3, PassiveType.Guard, false);
        second.GetPlayer(0).Hand.Add(changedCard);
        CardData changed = changedCard.Data;
        changed.aiBasePriority = 999;
        changed.aiRole = CardAIRole.Finisher;
        changed.aiPlayStyle = AIPlayStyle.Aggressive;
        changed.aiTargetPriority = AITargetPriority.EnemyHero;
        changed.aiLethalBonus = 999;

        CollectionAssert.AreEqual(
            AIFeatureEncoder.EncodeState(first, 0),
            AIFeatureEncoder.EncodeState(second, 0));
    }

    [Test]
    public void ApplyAction_RefreshesMaterializedHiddenCounts()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        PlayerStateSnapshot player = state.GetPlayer(0);
        player.HandIsHidden = true;
        player.HiddenInformationMaterialized = true;
        player.HiddenHandCount = 99;
        player.HiddenDeckCount = 99;
        player.Hand.Add(SimulationTestHelpers.CreateCard(10, 0, CardState.Hand, 2, 3, PassiveType.None, false));

        SimulatedAction play = new BattleStateSimulator().GenerateLegalActions(state)
            .Find(action => action.Type == SimulatedActionType.PlayHandCard);
        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(state, play, new Random(1));

        Assert.AreEqual(result.GetPlayer(0).Hand.Count, result.GetPlayer(0).HiddenHandCount);
        Assert.AreEqual(result.GetPlayer(0).DeckRemaining.Count, result.GetPlayer(0).HiddenDeckCount);
    }

    [Test]
    public void PuctSelection_ConvertsOpponentValueToParentPerspective()
    {
        PUCTNode root = new(null, null, 0, 1f);
        root.Expand(
            new List<SimulatedAction>
            {
                new() { Type = SimulatedActionType.EndTurn },
                new() { Type = SimulatedActionType.PlayHandCard, SourceCardId = 1 },
            },
            new List<float> { 0.5f, 0.5f });
        PUCTNode opponentChild = root.Children[0];
        opponentChild.ResolvePlayerIndex(1);
        opponentChild.Backpropagate(1);
        PUCTNode samePlayerChild = root.Children[1];
        samePlayerChild.Backpropagate(0);

        Assert.AreSame(samePlayerChild, root.SelectChild(0), "Opponent-positive value must be negative to the parent.");
    }

    [Test]
    public void NeuralPlanner_SelectsImmediateLethal()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(1).Health = 3;
        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(10, 0, CardState.Field, 5, 4, PassiveType.None, true));

        using NeuralMCTSPlanner planner = new(
            new LethalPriorProvider(),
            new NeuralMCTSSettings
            {
                Iterations = 80,
                TimeBudgetMs = 1000,
                DeterminizationCount = 1,
                MaxSearchDepth = 8,
            },
            123);

        MCTSResult result = planner.Search(state);

        Assert.IsNotNull(result.SelectedAction);
        Assert.AreEqual(SimulatedActionType.AttackPlayer, result.SelectedAction.Type);
        Assert.Greater(result.CompletedIterations, 1);
    }

    [Test]
    public void ResilientPlanner_UsesLegacyFallbackWhenModelIsMissing()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        using ResilientAIPlanner planner = new(
            null,
            new MCTSPlanner(new MCTSSettings { Iterations = 200, TimeBudgetMs = 1000 }, 1),
            "model missing");

        MCTSResult result = planner.Search(state);

        Assert.IsTrue(result.UsedFallback);
        Assert.AreEqual("model missing", result.FallbackReason);
        Assert.AreEqual(SimulatedActionType.EndTurn, result.SelectedAction.Type);
    }

    private static CardData CreateCardData(int index, int attack, int health)
    {
        return new CardData
        {
            index = index,
            cardType = CardType.Minion,
            cost = 1,
            attack = attack,
            health = health,
            passiveTypes = new List<PassiveType>(),
            effects = new List<CardEffectData>(),
        };
    }

    private sealed class LethalPriorProvider : IPolicyValueProvider
    {
        public bool IsReady => true;
        public string ModelVersion => "test";

        public PolicyValueEvaluation Evaluate(
            BattleStateSnapshot state,
            int observerPlayerIndex,
            IReadOnlyList<SimulatedAction> legalActions)
        {
            List<float> priors = new(legalActions.Count);
            float total = 0;
            foreach (SimulatedAction action in legalActions)
            {
                float prior = action.Type == SimulatedActionType.AttackPlayer ? 1f : 0.01f;
                priors.Add(prior);
                total += prior;
            }
            for (int index = 0; index < priors.Count; index++)
            {
                priors[index] /= total;
            }

            return new PolicyValueEvaluation
            {
                Success = true,
                ModelVersion = ModelVersion,
                Value = 0,
                Priors = priors,
            };
        }

        public void Dispose()
        {
        }
    }
}
