using System.IO;
using NUnit.Framework;
using UnityEditor;

public class ModelPromotionArenaTests
{
    private const string TestConfigPath = "Assets/AI/Configs/TestRefreshConfig.asset";

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(TestConfigPath);
        AssetDatabase.Refresh();
    }

    [Test]
    public void RefreshConfig_Parameterized_UpdatesConfigFromPromotedModels()
    {
        if (!File.Exists("Assets/AI/Models/policy.onnx") ||
            !File.Exists("Assets/AI/Models/value.onnx") ||
            !File.Exists("Assets/AI/Models/manifest.json"))
        {
            Assert.Ignore("Promoted model files are not present.");
        }

        AIModelConfig config = ModelPromotionUtility.RefreshConfig("Assets/AI/Models", TestConfigPath);
        Assert.NotNull(config);
        Assert.IsFalse(string.IsNullOrWhiteSpace(config.modelVersion));
        Assert.IsFalse(string.IsNullOrWhiteSpace(config.modelChecksum));
        Assert.IsTrue(config.Validate(out string error), error);
    }

    [Test]
    public void RefreshDefaultConfig_ProducesValidRuntimeConfig()
    {
        if (!File.Exists("Assets/AI/Models/policy.onnx") ||
            !File.Exists("Assets/AI/Models/value.onnx") ||
            !File.Exists("Assets/AI/Models/manifest.json"))
        {
            Assert.Ignore("Promoted model files are not present.");
        }

        ModelPromotionUtility.RefreshDefaultConfig();
        AIModelConfig config = AssetDatabase.LoadAssetAtPath<AIModelConfig>(ModelPromotionUtility.ConfigPath);
        Assert.NotNull(config, "Default model config should exist.");
        Assert.IsTrue(config.Validate(out string error), error);
    }

    [Test]
    public void ArenaReportJson_IncludesOpponentAndChampionWins()
    {
        ArenaReport report = new()
        {
            ModelVersion = "self-play-r001-candidate-001",
            Opponent = "teacher-v1-001",
            Games = 1000,
            CandidateWins = 600,
            LegacyWins = 400,
            ChampionWins = 400,
            Draws = 0,
            CandidateScoreRate = 0.6,
            CandidateRawWinRate = 0.6,
            DecisionP95Milliseconds = 40.0,
            PassedWinRate = true,
            PassedLatency = true,
            PassedMinimumGames = true,
            PromotionPassed = true,
        };

        string json = ArenaRunner.BuildReportJson(report);
        StringAssert.Contains("\"opponent\": \"teacher-v1-001\"", json);
        StringAssert.Contains("\"championWins\": 400", json);
        StringAssert.Contains("\"legacyWins\": 400", json);
    }
}
