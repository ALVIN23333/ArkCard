using System;
using NUnit.Framework;

public class DeckMatrixTests
{
    [Test]
    public void ParseDeckMatrix_All_ExpandsEveryDeck()
    {
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, TrainingMatchRunner.ParseDeckMatrix("all", 3));
    }

    [Test]
    public void ParseDeckMatrix_List_ParsesIndices()
    {
        CollectionAssert.AreEqual(new[] { 0, 1 }, TrainingMatchRunner.ParseDeckMatrix("0,1", 2));
        CollectionAssert.AreEqual(new[] { 1, 0 }, TrainingMatchRunner.ParseDeckMatrix("1, 0", 2));
    }

    [Test]
    public void ParseDeckMatrix_NullOrEmpty_ReturnsNull()
    {
        Assert.IsNull(TrainingMatchRunner.ParseDeckMatrix(null, 3));
        Assert.IsNull(TrainingMatchRunner.ParseDeckMatrix(string.Empty, 3));
        Assert.IsNull(TrainingMatchRunner.ParseDeckMatrix("   ", 3));
    }

    [Test]
    public void ParseDeckMatrix_Invalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => TrainingMatchRunner.ParseDeckMatrix("0,5", 2));
        Assert.Throws<ArgumentException>(() => TrainingMatchRunner.ParseDeckMatrix("-1", 2));
        Assert.Throws<ArgumentException>(() => TrainingMatchRunner.ParseDeckMatrix("abc", 2));
        Assert.Throws<ArgumentException>(() => TrainingMatchRunner.ParseDeckMatrix("all", 0));
    }

    [Test]
    public void ComputeDeckPair_CoversAllOrderedPairsForTwoDecks()
    {
        int[] matrix = { 0, 1 };
        var pairs = new System.Collections.Generic.HashSet<(int, int)>();
        for (int gameId = 0; gameId < 4; gameId++)
        {
            pairs.Add(TrainingMatchRunner.ComputeDeckPair(matrix, gameId));
        }
        Assert.AreEqual(4, pairs.Count);
        Assert.IsTrue(pairs.Contains((0, 0)));
        Assert.IsTrue(pairs.Contains((0, 1)));
        Assert.IsTrue(pairs.Contains((1, 0)));
        Assert.IsTrue(pairs.Contains((1, 1)));
    }

    [Test]
    public void ComputeDeckPair_IsDeterministic()
    {
        int[] matrix = { 0, 1, 2 };
        Assert.AreEqual(
            TrainingMatchRunner.ComputeDeckPair(matrix, 7),
            TrainingMatchRunner.ComputeDeckPair(matrix, 7));
    }

    [Test]
    public void ReadStringArgument_ParsesCommandLineStyleArgs()
    {
        string[] arguments =
        {
            "-aiCandidateModelsDir", "Assets/AI/Models/Candidate",
            "-aiCandidateConfigPath", "Assets/AI/Configs/CandidateAIModelConfig.asset",
        };
        Assert.AreEqual(
            "Assets/AI/Models/Candidate",
            ModelPromotionUtility.ReadStringArgument(arguments, "-aiCandidateModelsDir", "fallback"));
        Assert.AreEqual(
            "Assets/AI/Configs/CandidateAIModelConfig.asset",
            ModelPromotionUtility.ReadStringArgument(arguments, "-aiCandidateConfigPath", "fallback"));
        Assert.AreEqual("fallback", ModelPromotionUtility.ReadStringArgument(arguments, "-aiMissing", "fallback"));
    }
}
