using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = System.Random;

[Serializable]
public sealed class ArenaRunOptions
{
    public int GameCount = 1000;
    public int MaxPliesPerGame = 256;
    public int Seed = 20260806;
    public int FirstDeckIndex = -1;
    public int SecondDeckIndex = -1;
    public string ModelConfigPath = "Assets/AI/Configs/DefaultAIModelConfig.asset";
    public string ChampionModelConfigPath = null;
    public string ReportPath = "Artifacts/AI/Reports/arena-report.json";
}

public sealed class ArenaReport
{
    public string ModelVersion = string.Empty;
    public string Opponent = "legacy";
    public int Games;
    public int CandidateWins;
    public int LegacyWins;
    public int ChampionWins;
    public int Draws;
    public double CandidateScoreRate;
    public double CandidateRawWinRate;
    public double DecisionP95Milliseconds;
    public bool PassedWinRate;
    public bool PassedLatency;
    public bool PassedMinimumGames;
    public bool PromotionPassed;
}

public static class ArenaRunner
{
    private const double RequiredScoreRate = 0.55;
    private const double MaximumP95Milliseconds = 50.0;
    private const int RequiredGameCount = 1000;

    [MenuItem("Tools/AI Training/Run 20-Game Arena Smoke Test")]
    public static void RunSmokeArena()
    {
        ArenaReport report = Run(new ArenaRunOptions
        {
            GameCount = 20,
            ReportPath = "Artifacts/AI/Reports/arena-smoke.json",
        });
        Debug.Log($"[AI Arena] Smoke test complete: score={report.CandidateScoreRate:P1}, P95={report.DecisionP95Milliseconds:F2} ms.");
    }

    public static void RunFromCommandLine()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        ArenaRunOptions options = new()
        {
            GameCount = ReadIntArgument(arguments, "-aiArenaGames", 1000),
            MaxPliesPerGame = ReadIntArgument(arguments, "-aiMaxPlies", 256),
            Seed = ReadIntArgument(arguments, "-aiSeed", 20260806),
            FirstDeckIndex = ReadIntArgument(arguments, "-aiFirstDeck", -1),
            SecondDeckIndex = ReadIntArgument(arguments, "-aiSecondDeck", -1),
            ModelConfigPath = ReadStringArgument(
                arguments,
                "-aiCandidateModelConfig",
                ReadStringArgument(arguments, "-aiModelConfig", "Assets/AI/Configs/DefaultAIModelConfig.asset")),
            ChampionModelConfigPath = ReadStringArgument(arguments, "-aiChampionModelConfig", null),
            ReportPath = ReadStringArgument(arguments, "-aiArenaReport", "Artifacts/AI/Reports/arena-report.json"),
        };
        ArenaReport report = Run(options);
        Debug.Log($"[AI Arena] Complete: games={report.Games}, score={report.CandidateScoreRate:P2}, P95={report.DecisionP95Milliseconds:F2} ms, promotion={report.PromotionPassed}");
    }

    public static ArenaReport Run(
        ArenaRunOptions options,
        AIModelConfig modelConfig = null,
        CardListSO cardDatabase = null,
        DeckListSO deckDatabase = null)
    {
        options ??= new ArenaRunOptions();
        if (options.GameCount <= 0 || (options.GameCount & 1) != 0 || options.MaxPliesPerGame <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Arena game count must be a positive even number and max plies must be positive.");
        }

        modelConfig = modelConfig != null ? modelConfig : AssetDatabase.LoadAssetAtPath<AIModelConfig>(options.ModelConfigPath);
        cardDatabase = cardDatabase != null ? cardDatabase : Resources.Load<CardListSO>("ArkCardsDatabase");
        deckDatabase = deckDatabase != null ? deckDatabase : Resources.Load<DeckListSO>("DeckListDatabase");
        if (modelConfig == null)
        {
            throw new InvalidOperationException($"AI model config is missing at {options.ModelConfigPath}.");
        }
        if (!modelConfig.Validate(out string modelError))
        {
            throw new InvalidOperationException(modelError);
        }
        AIModelConfig championConfig = null;
        if (!string.IsNullOrWhiteSpace(options.ChampionModelConfigPath))
        {
            championConfig = AssetDatabase.LoadAssetAtPath<AIModelConfig>(options.ChampionModelConfigPath);
            if (championConfig == null)
            {
                throw new InvalidOperationException($"Champion AI model config is missing at {options.ChampionModelConfigPath}.");
            }
            if (!championConfig.Validate(out string championError))
            {
                throw new InvalidOperationException(championError);
            }
        }
        if (cardDatabase == null || deckDatabase == null || deckDatabase.decks == null || deckDatabase.decks.Count == 0)
        {
            throw new InvalidOperationException("Arena card or deck database is missing.");
        }

        int firstIndex = ResolveDeckIndex(options.FirstDeckIndex, deckDatabase.playerDeckIndex, 0, deckDatabase.decks.Count);
        int secondIndex = ResolveDeckIndex(options.SecondDeckIndex, deckDatabase.aiDeckIndex, deckDatabase.decks.Count > 1 ? 1 : 0, deckDatabase.decks.Count);
        int[] candidateDeck = GetDeck(deckDatabase, firstIndex);
        int[] legacyDeck = GetDeck(deckDatabase, secondIndex);
        List<double> candidateDecisionTimes = new();
        ArenaReport report = new()
        {
            ModelVersion = modelConfig.modelVersion,
            Opponent = championConfig != null ? championConfig.modelVersion : "legacy",
        };

        int pairCount = options.GameCount / 2;
        for (int pair = 0; pair < pairCount; pair++)
        {
            int seed = unchecked(options.Seed + pair * 104729);
            CountResult(
                PlayGame(seed, 0, candidateDeck, legacyDeck, cardDatabase, modelConfig, championConfig, options.MaxPliesPerGame, candidateDecisionTimes),
                report);
            CountResult(
                PlayGame(seed, 1, legacyDeck, candidateDeck, cardDatabase, modelConfig, championConfig, options.MaxPliesPerGame, candidateDecisionTimes),
                report);
        }

        report.Games = report.CandidateWins + report.LegacyWins + report.Draws;
        report.ChampionWins = championConfig != null ? report.LegacyWins : 0;
        report.CandidateRawWinRate = report.Games > 0 ? (double)report.CandidateWins / report.Games : 0;
        report.CandidateScoreRate = report.Games > 0 ? (report.CandidateWins + 0.5 * report.Draws) / report.Games : 0;
        report.DecisionP95Milliseconds = Percentile(candidateDecisionTimes, 0.95);
        report.PassedWinRate = report.CandidateScoreRate >= RequiredScoreRate;
        report.PassedLatency = report.DecisionP95Milliseconds <= MaximumP95Milliseconds;
        report.PassedMinimumGames = report.Games >= RequiredGameCount;
        report.PromotionPassed = report.PassedWinRate && report.PassedLatency && report.PassedMinimumGames;
        WriteReport(options.ReportPath, report);
        return report;
    }

    private static ArenaGameResult PlayGame(
        int seed,
        int candidatePlayerIndex,
        int[] firstDeck,
        int[] secondDeck,
        CardListSO cardDatabase,
        AIModelConfig modelConfig,
        AIModelConfig championConfig,
        int maxPlies,
        List<double> candidateDecisionTimes)
    {
        BattleStateSnapshot state = TrainingSimulation.CreateInitialState(firstDeck, secondDeck, cardDatabase, seed);
        Random effectsRandom = new(seed);
        IAIPlanner candidate = new NeuralMCTSPlanner(
            new BarracudaPolicyValueProvider(modelConfig),
            modelConfig.CreateSearchSettings(),
            seed);
        IAIPlanner legacy = championConfig != null
            ? new NeuralMCTSPlanner(
                new BarracudaPolicyValueProvider(championConfig),
                championConfig.CreateSearchSettings(),
                seed)
            : new MCTSPlanner(new MCTSSettings
            {
                Iterations = 300,
                TimeBudgetMs = 35,
                RolloutActionLimit = 4,
                ExplorationConstant = 1.4,
                ExpandTopCandidatesBias = 3,
                MaxRootTurns = 2,
                MaxActionsPerNode = 10,
            }, seed);

        try
        {
            for (int ply = 0; ply < maxPlies && !state.IsGameOver; ply++)
            {
                int player = state.CurrentPlayerIndex;
                BattleStateSnapshot observation = TrainingSimulation.CreateObservation(state, player);
                IAIPlanner planner = player == candidatePlayerIndex ? candidate : legacy;
                Stopwatch decision = Stopwatch.StartNew();
                MCTSResult search = planner.Search(observation);
                decision.Stop();
                if (player == candidatePlayerIndex)
                {
                    candidateDecisionTimes.Add(decision.Elapsed.TotalMilliseconds);
                }
                if (search.SelectedAction == null)
                {
                    break;
                }
                state = TrainingSimulation.ApplyAuthoritativeAction(state, search.SelectedAction, effectsRandom);
            }
        }
        finally
        {
            DisposePlanner(candidate);
            DisposePlanner(legacy);
        }

        int winner = TrainingSimulation.GetWinnerPlayerIndex(state);
        return new ArenaGameResult
        {
            CandidateWon = winner == candidatePlayerIndex,
            LegacyWon = winner >= 0 && winner != candidatePlayerIndex,
        };
    }

    private static void CountResult(ArenaGameResult game, ArenaReport report)
    {
        if (game.CandidateWon) report.CandidateWins++;
        else if (game.LegacyWon) report.LegacyWins++;
        else report.Draws++;
    }

    private static void DisposePlanner(IAIPlanner planner)
    {
        if (planner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return double.PositiveInfinity;
        values.Sort();
        int index = (int)Math.Ceiling(percentile * values.Count) - 1;
        return values[Math.Max(0, Math.Min(values.Count - 1, index))];
    }

    private static void WriteReport(string reportPath, ArenaReport report)
    {
        string fullPath = TrainingSimulation.ResolveProjectPath(reportPath);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, BuildReportJson(report), new UTF8Encoding(false));
    }

    public static string BuildReportJson(ArenaReport report)
    {
        StringBuilder json = new();
        json.AppendLine("{");
        json.Append("  \"modelVersion\": \"").Append(EscapeJson(report.ModelVersion)).AppendLine("\",");
        json.Append("  \"opponent\": \"").Append(EscapeJson(report.Opponent)).AppendLine("\",");
        json.Append("  \"games\": ").Append(report.Games).AppendLine(",");
        json.Append("  \"candidateWins\": ").Append(report.CandidateWins).AppendLine(",");
        json.Append("  \"legacyWins\": ").Append(report.LegacyWins).AppendLine(",");
        json.Append("  \"championWins\": ").Append(report.ChampionWins).AppendLine(",");
        json.Append("  \"draws\": ").Append(report.Draws).AppendLine(",");
        json.Append("  \"candidateScoreRate\": ").Append(report.CandidateScoreRate.ToString("R", CultureInfo.InvariantCulture)).AppendLine(",");
        json.Append("  \"candidateRawWinRate\": ").Append(report.CandidateRawWinRate.ToString("R", CultureInfo.InvariantCulture)).AppendLine(",");
        json.Append("  \"decisionP95Milliseconds\": ").Append(report.DecisionP95Milliseconds.ToString("R", CultureInfo.InvariantCulture)).AppendLine(",");
        json.Append("  \"passedWinRate\": ").Append(report.PassedWinRate.ToString().ToLowerInvariant()).AppendLine(",");
        json.Append("  \"passedLatency\": ").Append(report.PassedLatency.ToString().ToLowerInvariant()).AppendLine(",");
        json.Append("  \"passedMinimumGames\": ").Append(report.PassedMinimumGames.ToString().ToLowerInvariant()).AppendLine(",");
        json.Append("  \"promotionPassed\": ").Append(report.PromotionPassed.ToString().ToLowerInvariant()).AppendLine();
        json.AppendLine("}");
        return json.ToString();
    }

    private static int[] GetDeck(DeckListSO database, int index)
    {
        DeckData deck = database.GetDeck(index);
        return deck != null && deck.deck != null ? deck.deck.ToArray() : Array.Empty<int>();
    }

    private static int ResolveDeckIndex(int requested, int configured, int fallback, int count)
    {
        if (requested >= 0 && requested < count) return requested;
        if (configured >= 0 && configured < count) return configured;
        return Math.Max(0, Math.Min(count - 1, fallback));
    }

    private static int ReadIntArgument(string[] arguments, string name, int fallback)
    {
        string value = ReadStringArgument(arguments, name, null);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    }

    private static string ReadStringArgument(string[] arguments, string name, string fallback)
    {
        for (int index = 0; index + 1 < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase)) return arguments[index + 1];
        }
        return fallback;
    }

    private static string EscapeJson(string value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private sealed class ArenaGameResult
    {
        public bool CandidateWon;
        public bool LegacyWon;
    }
}
