using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = System.Random;

[Serializable]
public sealed class TrainingRunOptions
{
    public int TargetDecisionSamples = 100000;
    public int MaxMatches = 10000;
    public int MaxPliesPerMatch = 256;
    public int SamplesPerShard = 2048;
    public int Seed = 20260806;
    public int TeacherIterations = 300;
    public int TeacherTimeBudgetMs = 35;
    public int TeacherRolloutActionLimit = 4;
    public int FirstDeckIndex = -1;
    public int SecondDeckIndex = -1;
    public string DeckMatrix = null;
    public string OutputDirectory = "Artifacts/AI/Datasets";
    public string Prefix = "legacy-teacher";
}

public sealed class TrainingRunSummary
{
    public int RequestedSamples;
    public int Samples;
    public int Matches;
    public int FirstPlayerWins;
    public int SecondPlayerWins;
    public int Draws;
    public string DeckMatrix = null;
    public int[] DeckIndices = null;
    public long ElapsedMilliseconds;
    public List<string> Shards = new();
}

public static class TrainingMatchRunner
{
    [MenuItem("Tools/AI Training/Generate 2K Teacher Smoke Dataset")]
    public static void GenerateSmokeDataset()
    {
        TrainingRunOptions options = new()
        {
            TargetDecisionSamples = 2048,
            MaxMatches = 512,
            SamplesPerShard = 2048,
            OutputDirectory = "Artifacts/AI/Datasets/smoke",
        };

        try
        {
            TrainingRunSummary summary = Run(options);
            Debug.Log($"[AI Training] Teacher smoke dataset complete: {summary.Samples} samples, {summary.Matches} matches, {summary.ElapsedMilliseconds} ms.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
    }

    public static void RunFromCommandLine()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        TrainingRunOptions options = new()
        {
            TargetDecisionSamples = ReadIntArgument(arguments, "-aiSamples", 100000),
            MaxMatches = ReadIntArgument(arguments, "-aiMaxMatches", 10000),
            MaxPliesPerMatch = ReadIntArgument(arguments, "-aiMaxPlies", 256),
            SamplesPerShard = ReadIntArgument(arguments, "-aiShardSamples", 2048),
            Seed = ReadIntArgument(arguments, "-aiSeed", 20260806),
            FirstDeckIndex = ReadIntArgument(arguments, "-aiFirstDeck", -1),
            SecondDeckIndex = ReadIntArgument(arguments, "-aiSecondDeck", -1),
            DeckMatrix = ReadStringArgument(arguments, "-aiDeckMatrix", null),
            OutputDirectory = ReadStringArgument(arguments, "-aiOutput", "Artifacts/AI/Datasets"),
            Prefix = ReadStringArgument(arguments, "-aiPrefix", "legacy-teacher"),
        };

        TrainingRunSummary summary = Run(options);
        Debug.Log($"[AI Training] Complete: samples={summary.Samples}, matches={summary.Matches}, elapsedMs={summary.ElapsedMilliseconds}");
    }

    public static void RunSelfPlayFromCommandLine()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        TrainingRunOptions options = new()
        {
            TargetDecisionSamples = ReadIntArgument(arguments, "-aiSamples", 100000),
            MaxMatches = ReadIntArgument(arguments, "-aiMaxMatches", 10000),
            MaxPliesPerMatch = ReadIntArgument(arguments, "-aiMaxPlies", 256),
            SamplesPerShard = ReadIntArgument(arguments, "-aiShardSamples", 2048),
            Seed = ReadIntArgument(arguments, "-aiSeed", 20260806),
            FirstDeckIndex = ReadIntArgument(arguments, "-aiFirstDeck", -1),
            SecondDeckIndex = ReadIntArgument(arguments, "-aiSecondDeck", -1),
            DeckMatrix = ReadStringArgument(arguments, "-aiDeckMatrix", null),
            OutputDirectory = ReadStringArgument(arguments, "-aiOutput", "Artifacts/AI/Datasets/self-play"),
            Prefix = ReadStringArgument(arguments, "-aiPrefix", "neural-self-play"),
        };
        string modelConfigPath = ReadStringArgument(
            arguments,
            "-aiModelConfig",
            "Assets/AI/Configs/DefaultAIModelConfig.asset");
        AIModelConfig modelConfig = AssetDatabase.LoadAssetAtPath<AIModelConfig>(modelConfigPath);
        TrainingRunSummary summary = RunSelfPlay(options, modelConfig);
        Debug.Log($"[AI Training] Self-play complete: samples={summary.Samples}, matches={summary.Matches}, elapsedMs={summary.ElapsedMilliseconds}");
    }

    public static TrainingRunSummary Run(
        TrainingRunOptions options,
        CardListSO cardDatabase = null,
        DeckListSO deckDatabase = null)
    {
        options ??= new TrainingRunOptions();
        PrepareTrainingInputs(
            options,
            ref cardDatabase,
            ref deckDatabase,
            out int[] configuredFirstDeck,
            out int[] configuredSecondDeck);
        int[] deckMatrix = ParseDeckMatrix(options.DeckMatrix, deckDatabase.decks != null ? deckDatabase.decks.Count : 0);
        return RunMatches(
            options,
            configuredFirstDeck,
            configuredSecondDeck,
            deckMatrix,
            deckDatabase,
            (gameId, gameSeed, firstDeck, secondDeck) =>
                PlayTeacherMatch(gameId, gameSeed, firstDeck, secondDeck, cardDatabase, options));
    }

    public static TrainingRunSummary RunSelfPlay(
        TrainingRunOptions options,
        AIModelConfig modelConfig,
        CardListSO cardDatabase = null,
        DeckListSO deckDatabase = null)
    {
        options ??= new TrainingRunOptions();
        if (modelConfig == null)
        {
            throw new InvalidOperationException("A promoted AIModelConfig is required for neural self-play.");
        }
        if (!modelConfig.Validate(out string modelError))
        {
            throw new InvalidOperationException(modelError);
        }
        PrepareTrainingInputs(
            options,
            ref cardDatabase,
            ref deckDatabase,
            out int[] configuredFirstDeck,
            out int[] configuredSecondDeck);
        int[] deckMatrix = ParseDeckMatrix(options.DeckMatrix, deckDatabase.decks != null ? deckDatabase.decks.Count : 0);
        return RunMatches(
            options,
            configuredFirstDeck,
            configuredSecondDeck,
            deckMatrix,
            deckDatabase,
            (gameId, gameSeed, firstDeck, secondDeck) =>
                PlayNeuralSelfPlayMatch(gameId, gameSeed, firstDeck, secondDeck, cardDatabase, modelConfig, options));
    }

    private static TrainingRunSummary RunMatches(
        TrainingRunOptions options,
        int[] configuredFirstDeck,
        int[] configuredSecondDeck,
        int[] deckMatrix,
        DeckListSO deckDatabase,
        Func<int, int, int[], int[], TrainingGameResult> playMatch)
    {
        string outputDirectory = TrainingSimulation.ResolveProjectPath(options.OutputDirectory);
        DatasetShardWriter shardWriter = new(outputDirectory, options.Prefix, options.SamplesPerShard);
        TrainingRunSummary summary = new() { RequestedSamples = options.TargetDecisionSamples };
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            for (int gameId = 0;
                 gameId < options.MaxMatches && shardWriter.TotalSamplesWritten < options.TargetDecisionSamples;
                 gameId++)
            {
                int gameSeed = unchecked(options.Seed + gameId * 104729);
                int[] firstDeck;
                int[] secondDeck;
                bool swapDecks = (gameId & 1) != 0;
                if (deckMatrix != null)
                {
                    (int firstIndex, int secondIndex) = ComputeDeckPair(deckMatrix, gameId);
                    firstDeck = GetMatrixDeck(deckDatabase, swapDecks ? secondIndex : firstIndex);
                    secondDeck = GetMatrixDeck(deckDatabase, swapDecks ? firstIndex : secondIndex);
                }
                else
                {
                    firstDeck = swapDecks ? configuredSecondDeck : configuredFirstDeck;
                    secondDeck = swapDecks ? configuredFirstDeck : configuredSecondDeck;
                }
                TrainingGameResult game = playMatch(gameId, gameSeed, firstDeck, secondDeck);
                foreach (TrainingSample sample in game.Samples)
                {
                    shardWriter.Write(sample);
                }

                summary.Matches++;
                if (game.WinnerPlayerIndex == 0) summary.FirstPlayerWins++;
                else if (game.WinnerPlayerIndex == 1) summary.SecondPlayerWins++;
                else summary.Draws++;
            }
        }
        finally
        {
            shardWriter.Dispose();
            stopwatch.Stop();
        }

        summary.Samples = shardWriter.TotalSamplesWritten;
        summary.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        summary.DeckMatrix = deckMatrix != null ? options.DeckMatrix : null;
        summary.DeckIndices = deckMatrix;
        summary.Shards.AddRange(shardWriter.CompletedShards);
        WriteSummary(outputDirectory, options.Prefix, summary);
        if (summary.Samples < options.TargetDecisionSamples)
        {
            throw new InvalidOperationException(
                $"Dataset generation stopped at {summary.Samples}/{options.TargetDecisionSamples} samples after {summary.Matches} matches. Increase MaxMatches.");
        }
        return summary;
    }

    private static TrainingGameResult PlayTeacherMatch(
        int gameId,
        int seed,
        int[] firstDeck,
        int[] secondDeck,
        CardListSO cardDatabase,
        TrainingRunOptions options)
    {
        BattleStateSnapshot state = TrainingSimulation.CreateInitialState(firstDeck, secondDeck, cardDatabase, seed);
        Random effectsRandom = new(seed);
        List<TrainingSample> samples = new();

        for (int ply = 0; ply < options.MaxPliesPerMatch && !state.IsGameOver; ply++)
        {
            int observer = state.CurrentPlayerIndex;
            BattleStateSnapshot observation = TrainingSimulation.CreateObservation(state, observer);
            int decisionSeed = unchecked(seed * 397 + ply * 7919 + observer);
            MCTSSettings settings = new()
            {
                Iterations = options.TeacherIterations,
                TimeBudgetMs = options.TeacherTimeBudgetMs,
                RolloutActionLimit = options.TeacherRolloutActionLimit,
                ExplorationConstant = 1.4,
                ExpandTopCandidatesBias = 3,
                MaxRootTurns = 2,
                MaxActionsPerNode = 10,
            };
            MCTSResult search = new MCTSPlanner(settings, decisionSeed).Search(observation);
            if (search.SelectedAction == null)
            {
                break;
            }

            samples.Add(BuildSample(gameId, seed, ply, observer, firstDeck, secondDeck, observation, search));
            state = TrainingSimulation.ApplyAuthoritativeAction(state, search.SelectedAction, effectsRandom);
        }

        int winner = TrainingSimulation.GetWinnerPlayerIndex(state);
        foreach (TrainingSample sample in samples)
        {
            sample.Outcome = winner < 0 ? 0f : sample.ObserverPlayerIndex == winner ? 1f : -1f;
        }
        return new TrainingGameResult { WinnerPlayerIndex = winner, Samples = samples };
    }

    private static TrainingGameResult PlayNeuralSelfPlayMatch(
        int gameId,
        int seed,
        int[] firstDeck,
        int[] secondDeck,
        CardListSO cardDatabase,
        AIModelConfig modelConfig,
        TrainingRunOptions options)
    {
        BattleStateSnapshot state = TrainingSimulation.CreateInitialState(firstDeck, secondDeck, cardDatabase, seed);
        Random effectsRandom = new(seed);
        List<TrainingSample> samples = new();
        IAIPlanner firstPlanner = null;
        IAIPlanner secondPlanner = null;
        try
        {
            firstPlanner = CreateSelfPlayPlanner(modelConfig, seed);
            secondPlanner = CreateSelfPlayPlanner(modelConfig, unchecked(seed + 48611));
            IAIPlanner[] planners = { firstPlanner, secondPlanner };
            for (int ply = 0; ply < options.MaxPliesPerMatch && !state.IsGameOver; ply++)
            {
                int observer = state.CurrentPlayerIndex;
                BattleStateSnapshot observation = TrainingSimulation.CreateObservation(state, observer);
                MCTSResult search = planners[observer].Search(observation);
                if (search.SelectedAction == null)
                {
                    break;
                }
                samples.Add(BuildSample(gameId, seed, ply, observer, firstDeck, secondDeck, observation, search));
                state = TrainingSimulation.ApplyAuthoritativeAction(state, search.SelectedAction, effectsRandom);
            }
        }
        finally
        {
            if (firstPlanner is IDisposable firstDisposable) firstDisposable.Dispose();
            if (secondPlanner is IDisposable secondDisposable) secondDisposable.Dispose();
        }

        int winner = TrainingSimulation.GetWinnerPlayerIndex(state);
        foreach (TrainingSample sample in samples)
        {
            sample.Outcome = winner < 0 ? 0f : sample.ObserverPlayerIndex == winner ? 1f : -1f;
        }
        return new TrainingGameResult { WinnerPlayerIndex = winner, Samples = samples };
    }

    private static IAIPlanner CreateSelfPlayPlanner(AIModelConfig modelConfig, int seed)
    {
        NeuralMCTSSettings settings = modelConfig.CreateSearchSettings();
        settings.AddRootExplorationNoise = true;
        settings.RootDirichletAlpha = 0.3f;
        settings.RootNoiseFraction = 0.25f;
        return new NeuralMCTSPlanner(new BarracudaPolicyValueProvider(modelConfig), settings, seed);
    }

    private static TrainingSample BuildSample(
        int gameId,
        int seed,
        int ply,
        int observer,
        int[] firstDeck,
        int[] secondDeck,
        BattleStateSnapshot observation,
        MCTSResult search)
    {
        BattleStateSimulator simulator = new();
        List<SimulatedAction> legalActions = simulator.GenerateLegalActions(observation);
        TrainingSample sample = new()
        {
            Seed = seed,
            GameId = gameId,
            Ply = ply,
            ObserverPlayerIndex = observer,
            FirstDeck = (int[])firstDeck.Clone(),
            SecondDeck = (int[])secondDeck.Clone(),
            StateFeatures = AIFeatureEncoder.EncodeState(observation, observer),
        };

        int totalVisits = 0;
        foreach (SimulatedAction action in legalActions)
        {
            MCTSActionStatistics statistics = search.RootStatistics.Find(candidate => candidate.Action != null && candidate.Action.Equals(action));
            int visits = statistics != null ? Math.Max(0, statistics.VisitCount) : 0;
            if (visits == 0 && search.SelectedAction.Equals(action) && search.SkippedSearch)
            {
                visits = 1;
            }
            totalVisits += visits;
            sample.Actions.Add(new TrainingActionLabel
            {
                Features = AIFeatureEncoder.EncodeAction(observation, observer, action),
                VisitCount = visits,
            });
        }

        if (totalVisits == 0)
        {
            int selectedIndex = legalActions.FindIndex(action => action.Equals(search.SelectedAction));
            if (selectedIndex < 0)
            {
                throw new InvalidOperationException("Planner selected an action outside the canonical legal action set.");
            }
            sample.Actions[selectedIndex].VisitCount = 1;
        }
        return sample;
    }

    private static void PrepareTrainingInputs(
        TrainingRunOptions options,
        ref CardListSO cardDatabase,
        ref DeckListSO deckDatabase,
        out int[] configuredFirstDeck,
        out int[] configuredSecondDeck)
    {
        ValidateOptions(options);
        cardDatabase = cardDatabase != null ? cardDatabase : Resources.Load<CardListSO>("ArkCardsDatabase");
        deckDatabase = deckDatabase != null ? deckDatabase : Resources.Load<DeckListSO>("DeckListDatabase");
        if (cardDatabase == null)
        {
            throw new InvalidOperationException("Card database Resources/ArkCardsDatabase is missing.");
        }
        if (deckDatabase == null || deckDatabase.decks == null || deckDatabase.decks.Count == 0)
        {
            throw new InvalidOperationException("Deck database Resources/DeckListDatabase has no decks.");
        }

        int firstDeckIndex = ResolveDeckIndex(options.FirstDeckIndex, deckDatabase.playerDeckIndex, 0, deckDatabase.decks.Count);
        int secondFallback = deckDatabase.decks.Count > 1 ? 1 : 0;
        int secondDeckIndex = ResolveDeckIndex(options.SecondDeckIndex, deckDatabase.aiDeckIndex, secondFallback, deckDatabase.decks.Count);
        configuredFirstDeck = GetDeck(deckDatabase, firstDeckIndex);
        configuredSecondDeck = GetDeck(deckDatabase, secondDeckIndex);
        ValidateDeck(configuredFirstDeck, cardDatabase, firstDeckIndex);
        ValidateDeck(configuredSecondDeck, cardDatabase, secondDeckIndex);
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

    public static int[] ParseDeckMatrix(string value, int deckCount)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        int[] indices;
        if (value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            indices = new int[Math.Max(0, deckCount)];
            for (int index = 0; index < deckCount; index++)
            {
                indices[index] = index;
            }
        }
        else
        {
            string[] parts = value.Split(',');
            indices = new int[parts.Length];
            for (int index = 0; index < parts.Length; index++)
            {
                string part = parts[index].Trim();
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
                    parsed < 0 || parsed >= deckCount)
                {
                    throw new ArgumentException(
                        $"Invalid deck matrix index '{part}' for deck count {deckCount}.");
                }
                indices[index] = parsed;
            }
        }

        if (indices.Length == 0)
        {
            throw new ArgumentException("Deck matrix must contain at least one deck index.");
        }
        return indices;
    }

    public static (int First, int Second) ComputeDeckPair(int[] matrix, int gameId)
    {
        if (matrix == null || matrix.Length == 0)
        {
            throw new ArgumentException("Deck matrix is empty.");
        }
        int count = matrix.Length;
        int pair = gameId % (count * count);
        return (matrix[pair / count], matrix[pair % count]);
    }

    private static int[] GetMatrixDeck(DeckListSO deckDatabase, int index)
    {
        DeckData deck = deckDatabase != null ? deckDatabase.GetDeck(index) : null;
        if (deck == null || deck.deck == null)
        {
            throw new InvalidOperationException($"Deck matrix resolved to a missing deck at index {index}.");
        }
        return deck.deck.ToArray();
    }

    private static void ValidateDeck(int[] deck, CardListSO database, int index)
    {
        if (deck == null || deck.Length == 0)
        {
            throw new InvalidOperationException($"Deck index {index} is empty.");
        }
        foreach (int cardId in deck)
        {
            if (database.GetData(cardId) == null)
            {
                throw new InvalidOperationException($"Deck index {index} references missing card id {cardId}.");
            }
        }
    }

    private static void ValidateOptions(TrainingRunOptions options)
    {
        if (options.TargetDecisionSamples <= 0 || options.MaxMatches <= 0 || options.MaxPliesPerMatch <= 0
            || options.SamplesPerShard <= 0 || options.TeacherIterations <= 0 || options.TeacherTimeBudgetMs <= 0
            || options.TeacherRolloutActionLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Training counts and budgets must be positive.");
        }
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
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }
        return fallback;
    }

    private static void WriteSummary(string outputDirectory, string prefix, TrainingRunSummary summary)
    {
        StringBuilder json = new();
        json.AppendLine("{");
        json.Append("  \"requestedSamples\": ").Append(summary.RequestedSamples).AppendLine(",");
        json.Append("  \"samples\": ").Append(summary.Samples).AppendLine(",");
        json.Append("  \"matches\": ").Append(summary.Matches).AppendLine(",");
        json.Append("  \"firstPlayerWins\": ").Append(summary.FirstPlayerWins).AppendLine(",");
        json.Append("  \"secondPlayerWins\": ").Append(summary.SecondPlayerWins).AppendLine(",");
        json.Append("  \"draws\": ").Append(summary.Draws).AppendLine(",");
        if (summary.DeckMatrix != null)
        {
            json.Append("  \"deckMatrix\": \"").Append(EscapeJson(summary.DeckMatrix)).AppendLine("\",");
        }
        if (summary.DeckIndices != null)
        {
            json.Append("  \"deckIndices\": [").Append(string.Join(", ", summary.DeckIndices)).AppendLine("],");
        }
        json.Append("  \"elapsedMilliseconds\": ").Append(summary.ElapsedMilliseconds).AppendLine();
        json.AppendLine("}");
        File.WriteAllText(Path.Combine(outputDirectory, prefix + "-summary.json"), json.ToString(), new UTF8Encoding(false));
    }

    private static string EscapeJson(string value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private sealed class TrainingGameResult
    {
        public int WinnerPlayerIndex;
        public List<TrainingSample> Samples;
    }
}

public static class TrainingSimulation
{
    public static BattleStateSnapshot CreateInitialState(
        IReadOnlyList<int> firstDeck,
        IReadOnlyList<int> secondDeck,
        CardListSO cardDatabase,
        int seed)
    {
        if (cardDatabase == null) throw new ArgumentNullException(nameof(cardDatabase));
        BattleStateSnapshot state = new() { CurrentPlayerIndex = 0, RootPlayerIndex = -1 };
        state.Players.Add(CreatePlayer(0, true));
        state.Players.Add(CreatePlayer(1, false));

        int nextRuntimeId = 1;
        AddDeck(state.GetPlayer(0), firstDeck, cardDatabase, ref nextRuntimeId);
        AddDeck(state.GetPlayer(1), secondDeck, cardDatabase, ref nextRuntimeId);
        Random random = new(seed);
        Shuffle(state.GetPlayer(0).DeckRemaining, random);
        Shuffle(state.GetPlayer(1).DeckRemaining, random);
        for (int draw = 0; draw < 5; draw++)
        {
            DrawOne(state.GetPlayer(0));
            DrawOne(state.GetPlayer(1));
        }

        PlayerStateSnapshot first = state.GetPlayer(0);
        first.MaxCost = 1;
        first.Cost = 1;
        DrawOne(first);
        return state;
    }

    public static BattleStateSnapshot CreateObservation(BattleStateSnapshot authoritativeState, int observerPlayerIndex)
    {
        BattleStateSnapshot observation = authoritativeState.Clone();
        observation.RootPlayerIndex = -1;
        observation.RootEndTurnCount = 0;
        observation.IsTurnEnded = false;
        foreach (PlayerStateSnapshot player in observation.Players)
        {
            if (player == null || player.PlayerIndex == observerPlayerIndex)
            {
                continue;
            }

            player.HandIsHidden = true;
            player.HiddenInformationMaterialized = false;
            player.HiddenHandCount = player.Hand.Count;
            player.HiddenDeckCount = player.DeckRemaining.Count;
            player.HiddenCardPool.Clear();
            AddCardData(player.Hand, player.HiddenCardPool);
            AddCardData(player.DeckRemaining, player.HiddenCardPool);
            player.Hand.Clear();
            player.DeckRemaining.Clear();
        }
        return observation;
    }

    public static BattleStateSnapshot ApplyAuthoritativeAction(
        BattleStateSnapshot authoritativeState,
        SimulatedAction action,
        Random random)
    {
        BattleStateSnapshot prepared = authoritativeState.Clone();
        prepared.RootPlayerIndex = prepared.CurrentPlayerIndex;
        prepared.RootEndTurnCount = 0;
        prepared.MaxRootTurns = int.MaxValue;
        prepared.IsTurnEnded = false;
        BattleStateSnapshot result = new BattleStateSimulator().ApplyAction(prepared, action, random);
        result.RootPlayerIndex = -1;
        result.RootEndTurnCount = 0;
        result.MaxRootTurns = 2;
        result.IsTurnEnded = false;
        return result;
    }

    public static int GetWinnerPlayerIndex(BattleStateSnapshot state)
    {
        int winner = -1;
        int livingPlayers = 0;
        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player != null && player.Health > 0)
            {
                livingPlayers++;
                winner = player.PlayerIndex;
            }
        }
        return state.IsGameOver && livingPlayers == 1 ? winner : -1;
    }

    public static string ResolveProjectPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
        {
            throw new InvalidOperationException("Could not resolve the Unity project root.");
        }
        return Path.GetFullPath(Path.Combine(projectRoot, path));
    }

    private static PlayerStateSnapshot CreatePlayer(int index, bool isMainPlayer)
    {
        return new PlayerStateSnapshot
        {
            PlayerIndex = index,
            IsMainPlayer = isMainPlayer,
            Health = GameConst.initalHealth,
            MaxHealth = GameConst.initalHealth,
        };
    }

    private static void AddDeck(
        PlayerStateSnapshot player,
        IReadOnlyList<int> deck,
        CardListSO database,
        ref int nextRuntimeId)
    {
        if (deck == null) return;
        foreach (int cardId in deck)
        {
            CardData data = database.GetData(cardId);
            if (data == null) continue;
            CardStateSnapshot card = new()
            {
                RuntimeId = nextRuntimeId++,
                OwnerIndex = player.PlayerIndex,
                Data = data,
            };
            card.ResetRuntimeState(CardState.Deck);
            player.DeckRemaining.Add(card);
        }
    }

    private static void DrawOne(PlayerStateSnapshot player)
    {
        if (player.DeckRemaining.Count == 0) return;
        CardStateSnapshot card = player.DeckRemaining[0];
        player.DeckRemaining.RemoveAt(0);
        if (player.Hand.Count >= GameConst.handMax)
        {
            card.ResetRuntimeState(CardState.Graveyard);
            player.Graveyard.Add(card);
            return;
        }
        card.ResetRuntimeState(CardState.Hand);
        player.Hand.Add(card);
    }

    private static void Shuffle(List<CardStateSnapshot> cards, Random random)
    {
        for (int index = cards.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            CardStateSnapshot temporary = cards[index];
            cards[index] = cards[swapIndex];
            cards[swapIndex] = temporary;
        }
    }

    private static void AddCardData(List<CardStateSnapshot> source, List<CardData> destination)
    {
        foreach (CardStateSnapshot card in source)
        {
            if (card != null && card.Data != null)
            {
                destination.Add(card.Data);
            }
        }
    }
}
