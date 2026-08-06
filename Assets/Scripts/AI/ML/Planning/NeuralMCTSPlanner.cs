using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public sealed class NeuralMCTSPlanner : IAIPlanner, IDisposable
{
    private readonly BattleStateSimulator simulator = new();
    private readonly IPolicyValueProvider provider;
    private readonly NeuralMCTSSettings settings;
    private readonly Random random;

    public NeuralMCTSPlanner(
        IPolicyValueProvider provider,
        NeuralMCTSSettings settings = null,
        int? seed = null)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.settings = settings ?? new NeuralMCTSSettings();
        this.settings.Clamp();
        random = seed.HasValue ? new Random(seed.Value) : new Random(unchecked(Environment.TickCount * 397));
    }

    public MCTSResult Search(BattleStateSnapshot rootState)
    {
        MCTSResult result = new() { ModelVersion = provider.ModelVersion };
        if (rootState == null)
        {
            return result;
        }
        if (!provider.IsReady)
        {
            throw new AIModelUnavailableException("Policy/value provider is not ready.");
        }

        List<SimulatedAction> rootLegalActions = simulator.GenerateLegalActions(rootState);
        result.LegalActionCount = rootLegalActions.Count;
        if (rootLegalActions.Count == 0)
        {
            return result;
        }
        if (rootLegalActions.Count == 1)
        {
            result.SelectedAction = rootLegalActions[0];
            result.SkippedSearch = true;
            result.RootStatistics.Add(new MCTSActionStatistics { Action = rootLegalActions[0], PriorHeuristic = 1 });
            return result;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        Dictionary<SimulatedAction, AggregateStatistics> aggregates = new();
        int completedIterations = 0;
        int determinizationCount = Math.Min(settings.DeterminizationCount, settings.Iterations);
        for (int determinization = 0;
             determinization < determinizationCount && stopwatch.ElapsedMilliseconds < settings.TimeBudgetMs;
             determinization++)
        {
            BattleStateSnapshot world = rootState.Clone();
            world.RootPlayerIndex = world.CurrentPlayerIndex;
            world.MaxRootTurns = settings.MaxRootTurns;
            world.Determinize(random);

            PUCTNode root = new(null, null, world.CurrentPlayerIndex, 1f);
            int targetIterations = settings.Iterations / determinizationCount
                + (determinization < settings.Iterations % determinizationCount ? 1 : 0);
            for (int iteration = 0;
                 iteration < targetIterations && stopwatch.ElapsedMilliseconds < settings.TimeBudgetMs;
                 iteration++)
            {
                RunSimulation(root, world);
                completedIterations++;
            }
            AggregateRoot(root, aggregates);
        }
        stopwatch.Stop();

        if (aggregates.Count == 0)
        {
            throw new AIModelUnavailableException("Neural search exhausted its time budget before producing a root policy.");
        }

        result.CompletedIterations = completedIterations;
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        foreach (SimulatedAction legal in rootLegalActions)
        {
            if (!aggregates.TryGetValue(legal, out AggregateStatistics aggregate))
            {
                aggregate = new AggregateStatistics();
            }
            result.RootStatistics.Add(new MCTSActionStatistics
            {
                Action = legal,
                VisitCount = aggregate.VisitCount,
                MeanValue = aggregate.ValueWeight > 0 ? aggregate.ValueSum / aggregate.ValueWeight : 0,
                PriorHeuristic = aggregate.PriorCount > 0 ? aggregate.PriorSum / aggregate.PriorCount : 0,
            });
        }

        result.RootStatistics = result.RootStatistics
            .OrderByDescending(statistic => statistic.VisitCount)
            .ThenByDescending(statistic => statistic.PriorHeuristic)
            .ThenBy(statistic => statistic.Action.Type)
            .ThenBy(statistic => statistic.Action.SourceCardId)
            .ToList();
        result.SelectedAction = result.RootStatistics[0].Action;
        return result;
    }

    public void Dispose()
    {
        provider.Dispose();
    }

    private void RunSimulation(PUCTNode root, BattleStateSnapshot rootState)
    {
        BattleStateSnapshot state = rootState.Clone();
        PUCTNode node = root;
        List<PUCTNode> path = new() { root };
        int depth = 0;

        while (node.Expanded && node.Children.Count > 0 && depth < settings.MaxSearchDepth && !state.IsGameOver && !state.IsTurnEnded)
        {
            PUCTNode selected = node.SelectChild(settings.ExplorationConstant);
            if (selected == null)
            {
                break;
            }
            state = simulator.ApplyAction(state, selected.IncomingAction, random);
            selected.ResolvePlayerIndex(state.CurrentPlayerIndex);
            node = selected;
            path.Add(node);
            depth++;
        }

        double value;
        int valuePerspective = state.CurrentPlayerIndex;
        if (state.IsGameOver)
        {
            value = GetTerminalValue(state, valuePerspective);
        }
        else if (state.IsTurnEnded)
        {
            PolicyValueEvaluation evaluation = provider.Evaluate(
                state,
                valuePerspective,
                Array.Empty<SimulatedAction>());
            if (!evaluation.Success)
            {
                throw new AIModelUnavailableException(evaluation.Error);
            }
            value = evaluation.Value;
        }
        else
        {
            List<SimulatedAction> legalActions = simulator.GenerateLegalActions(state);
            if (legalActions.Count == 0)
            {
                value = 0;
            }
            else
            {
                PolicyValueEvaluation evaluation = provider.Evaluate(state, valuePerspective, legalActions);
                if (!evaluation.Success || evaluation.Priors.Count != legalActions.Count)
                {
                    throw new AIModelUnavailableException(evaluation.Error);
                }
                if (node == root && settings.AddRootExplorationNoise)
                {
                    ApplyRootNoise(evaluation.Priors);
                }
                node.Expand(legalActions, evaluation.Priors);
                value = evaluation.Value;
            }
        }

        Backpropagate(path, value, valuePerspective);
    }

    private static void Backpropagate(List<PUCTNode> path, double value, int valuePerspective)
    {
        double propagatedValue = value;
        int perspective = valuePerspective;
        for (int index = path.Count - 1; index >= 0; index--)
        {
            PUCTNode node = path[index];
            int nodePerspective = node.GetResolvedPlayerIndex();
            if (nodePerspective < 0)
            {
                nodePerspective = perspective;
            }
            if (nodePerspective != perspective)
            {
                propagatedValue = -propagatedValue;
            }
            node.Backpropagate(propagatedValue);
            perspective = nodePerspective;
        }
    }

    private static double GetTerminalValue(BattleStateSnapshot state, int playerIndex)
    {
        PlayerStateSnapshot player = state.GetPlayer(playerIndex);
        if (player == null || player.Health <= 0)
        {
            return -1;
        }

        bool livingOpponent = false;
        foreach (PlayerStateSnapshot candidate in state.Players)
        {
            if (candidate != null && candidate.PlayerIndex != playerIndex && candidate.Health > 0)
            {
                livingOpponent = true;
                break;
            }
        }
        return livingOpponent ? 0 : 1;
    }

    private static void AggregateRoot(
        PUCTNode root,
        Dictionary<SimulatedAction, AggregateStatistics> aggregates)
    {
        foreach (PUCTNode child in root.Children)
        {
            if (!aggregates.TryGetValue(child.IncomingAction, out AggregateStatistics aggregate))
            {
                aggregate = new AggregateStatistics();
                aggregates.Add(child.IncomingAction, aggregate);
            }
            aggregate.VisitCount += child.VisitCount;
            aggregate.ValueSum += child.MeanValue * child.VisitCount;
            aggregate.ValueWeight += child.VisitCount;
            aggregate.PriorSum += child.Prior;
            aggregate.PriorCount++;
        }
    }

    private void ApplyRootNoise(List<float> priors)
    {
        double[] noise = SampleDirichlet(priors.Count, settings.RootDirichletAlpha);
        for (int index = 0; index < priors.Count; index++)
        {
            priors[index] = (1f - settings.RootNoiseFraction) * priors[index]
                + settings.RootNoiseFraction * (float)noise[index];
        }
    }

    private double[] SampleDirichlet(int count, double alpha)
    {
        double[] samples = new double[count];
        double sum = 0;
        for (int index = 0; index < count; index++)
        {
            samples[index] = SampleGamma(alpha);
            sum += samples[index];
        }
        if (sum <= 0)
        {
            for (int index = 0; index < count; index++)
            {
                samples[index] = 1.0 / count;
            }
            return samples;
        }
        for (int index = 0; index < count; index++)
        {
            samples[index] /= sum;
        }
        return samples;
    }

    private double SampleGamma(double shape)
    {
        if (shape < 1)
        {
            double sample = SampleGamma(shape + 1);
            return sample * Math.Pow(Math.Max(double.Epsilon, random.NextDouble()), 1.0 / shape);
        }

        double d = shape - 1.0 / 3.0;
        double c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            double x = SampleStandardNormal();
            double v = 1 + c * x;
            if (v <= 0)
            {
                continue;
            }
            v = v * v * v;
            double uniform = random.NextDouble();
            if (uniform < 1 - 0.0331 * x * x * x * x
                || Math.Log(uniform) < 0.5 * x * x + d * (1 - v + Math.Log(v)))
            {
                return d * v;
            }
        }
    }

    private double SampleStandardNormal()
    {
        double first = Math.Max(double.Epsilon, random.NextDouble());
        double second = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(first)) * Math.Cos(2.0 * Math.PI * second);
    }

    private sealed class AggregateStatistics
    {
        public int VisitCount;
        public double ValueSum;
        public int ValueWeight;
        public double PriorSum;
        public int PriorCount;
    }
}
