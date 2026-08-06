using System;
using System.IO;
using NUnit.Framework;

public class DatasetShardTests
{
    [Test]
    public void DatasetShard_CSharpRoundTripPreservesSchemaAndLabels()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ArkCard-AI-Dataset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            DatasetShardWriter writer = new(directory, "roundtrip", 8);
            TrainingSample expected = new()
            {
                Seed = 123456789L,
                GameId = 7,
                Ply = 13,
                ObserverPlayerIndex = 1,
                Outcome = -1f,
                FirstDeck = new[] { 1, 2, 3 },
                SecondDeck = new[] { 4, 5 },
                StateFeatures = new float[AIEncodingSchema.StateFeatureCount],
            };
            expected.StateFeatures[0] = 0.25f;
            expected.StateFeatures[expected.StateFeatures.Length - 1] = -0.5f;
            TrainingActionLabel action = new()
            {
                Features = new float[AIEncodingSchema.ActionFeatureCount],
                VisitCount = 17,
            };
            action.Features[3] = 1f;
            action.Features[action.Features.Length - 1] = 0.75f;
            expected.Actions.Add(action);

            writer.Write(expected);
            writer.Dispose();

            Assert.AreEqual(1, writer.CompletedShards.Count);
            var actual = DatasetShardReader.ReadAll(writer.CompletedShards[0]);
            Assert.AreEqual(1, actual.Count);
            Assert.AreEqual(expected.Seed, actual[0].Seed);
            Assert.AreEqual(expected.GameId, actual[0].GameId);
            Assert.AreEqual(expected.Ply, actual[0].Ply);
            Assert.AreEqual(expected.ObserverPlayerIndex, actual[0].ObserverPlayerIndex);
            Assert.AreEqual(expected.Outcome, actual[0].Outcome);
            CollectionAssert.AreEqual(expected.FirstDeck, actual[0].FirstDeck);
            CollectionAssert.AreEqual(expected.SecondDeck, actual[0].SecondDeck);
            CollectionAssert.AreEqual(expected.StateFeatures, actual[0].StateFeatures);
            Assert.AreEqual(17, actual[0].Actions[0].VisitCount);
            CollectionAssert.AreEqual(expected.Actions[0].Features, actual[0].Actions[0].Features);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
