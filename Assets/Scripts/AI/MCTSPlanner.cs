using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

[Serializable]
public sealed class MCTSSettings
{
    public int Iterations = 400;
    public int TimeBudgetMs = 50;
    public double ExplorationConstant = 1.4;
    public int RolloutActionLimit = 8;
    public int ExpandTopCandidatesBias = 3;
    public int MaxRootTurns = 2;
    public int MaxActionsPerNode = 10;

    public void Clamp()
    {
        Iterations = Math.Max(200, Math.Min(500, Iterations));
        TimeBudgetMs = Math.Max(1, TimeBudgetMs);
        ExplorationConstant = Math.Max(0.01, ExplorationConstant);
        RolloutActionLimit = Math.Max(1, RolloutActionLimit);
        ExpandTopCandidatesBias = Math.Max(1, ExpandTopCandidatesBias);
        MaxRootTurns = Math.Max(1, Math.Min(3, MaxRootTurns));
        MaxActionsPerNode = Math.Max(1, Math.Min(30, MaxActionsPerNode));
    }
}

public sealed class MCTSActionStatistics
{
    public SimulatedAction Action;
    public int VisitCount;
    public double MeanValue;
    public double PriorHeuristic;
}

public sealed class MCTSResult
{
    public SimulatedAction SelectedAction;
    public int LegalActionCount;
    public int CompletedIterations;
    public long ElapsedMilliseconds;
    public bool SkippedSearch;
    public List<MCTSActionStatistics> RootStatistics = new();

    public string GetDebugSummary(BattleStateSnapshot rootState)
    {
        StringBuilder builder = new();
        builder.Append("[AI MCTS] root: ").Append(rootState != null ? rootState.GetSummary() : "null").AppendLine();
        builder.Append("legal=").Append(LegalActionCount)
            .Append(", iterations=").Append(CompletedIterations)
            .Append(", elapsedMs=").Append(ElapsedMilliseconds)
            .Append(", skipped=").Append(SkippedSearch).AppendLine();
        foreach (MCTSActionStatistics statistics in RootStatistics.Take(5))
        {
            builder.Append("  ").Append(statistics.Action)
                .Append(" visits=").Append(statistics.VisitCount)
                .Append(" mean=").Append(statistics.MeanValue.ToString("F3"))
                .Append(" prior=").Append(statistics.PriorHeuristic.ToString("F2")).AppendLine();
        }
        builder.Append("selected=").Append(SelectedAction != null ? SelectedAction.ToString() : "none");
        return builder.ToString();
    }
}

public sealed class MCTSPlanner
{
    private readonly BattleStateSimulator simulator;
    private readonly MCTSSettings settings;
    private readonly Random random;

    public MCTSPlanner(MCTSSettings settings = null, int? seed = null)
    {
        this.settings = settings ?? new MCTSSettings();
        this.settings.Clamp();
        simulator = new BattleStateSimulator();
        random = seed.HasValue ? new Random(seed.Value) : new Random(unchecked(Environment.TickCount * 397));
    }

    public MCTSResult Search(BattleStateSnapshot rootState)
    {
        MCTSResult result = new();
        if (rootState == null) return result;

        BattleStateSnapshot world = rootState.Clone();
        world.RootPlayerIndex = world.CurrentPlayerIndex;
        world.MaxRootTurns = settings.MaxRootTurns;
        world.Determinize(random);

        List<SimulatedAction> rootLegalActions = simulator.GenerateLegalActions(world);
        result.LegalActionCount = rootLegalActions.Count;
        if (rootLegalActions.Count == 0) return result;
        if (rootLegalActions.Count == 1)
        {
            result.SelectedAction = rootLegalActions[0];
            result.SkippedSearch = true;
            result.RootStatistics.Add(new MCTSActionStatistics
            {
                Action = rootLegalActions[0],
                PriorHeuristic = rootLegalActions[0].PriorHeuristic,
            });
            return result;
        }

        MCTSNode root = new(null, null) { PlayerIndex = world.CurrentPlayerIndex };
        root.SynchronizeLegalActions(rootLegalActions, settings.MaxActionsPerNode);
        Stopwatch stopwatch = Stopwatch.StartNew();
        int iterations = 0;
        while (iterations < settings.Iterations && (iterations == 0 || stopwatch.ElapsedMilliseconds < settings.TimeBudgetMs))
        {
            RunIteration(root, world, random);
            iterations++;
        }
        stopwatch.Stop();

        result.CompletedIterations = iterations;
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        foreach (MCTSNode child in root.Children)
        {
            result.RootStatistics.Add(new MCTSActionStatistics
            {
                Action = child.IncomingAction,
                VisitCount = child.VisitCount,
                MeanValue = child.MeanValue,
                PriorHeuristic = child.PriorHeuristic,
            });
        }
        foreach (SimulatedAction action in rootLegalActions)
        {
            if (!result.RootStatistics.Any(item => item.Action.Equals(action)))
            {
                result.RootStatistics.Add(new MCTSActionStatistics { Action = action, PriorHeuristic = action.PriorHeuristic });
            }
        }
        result.RootStatistics = result.RootStatistics
            .OrderByDescending(item => item.VisitCount)
            .ThenByDescending(item => item.MeanValue)
            .ThenByDescending(item => item.PriorHeuristic)
            .ToList();
        SimulatedAction selectedAction = result.RootStatistics[0].Action;
        if (selectedAction.Type == SimulatedActionType.EndTurn)
        {
            // When passing and attacking are close in value, prefer attacking: the casual AI
            // should press with its board rather than end the turn on a near-tie.
            MCTSActionStatistics bestAttack = null;
            foreach (MCTSActionStatistics statistics in result.RootStatistics)
            {
                if (statistics.VisitCount <= 0
                    || (statistics.Action.Type != SimulatedActionType.AttackPlayer
                        && statistics.Action.Type != SimulatedActionType.AttackMinion))
                {
                    continue;
                }
                if (bestAttack == null || statistics.MeanValue > bestAttack.MeanValue)
                {
                    bestAttack = statistics;
                }
            }
            if (bestAttack != null && result.RootStatistics[0].MeanValue - bestAttack.MeanValue < 0.05)
            {
                selectedAction = bestAttack.Action;
            }
        }
        result.SelectedAction = selectedAction;
        return result;
    }

    private void RunIteration(MCTSNode root, BattleStateSnapshot rootState, Random random)
    {
        BattleStateSnapshot state = rootState.Clone();
        MCTSNode node = root;
        List<MCTSNode> path = new() { root };
        int actionDepth = 0;

        while (actionDepth < settings.RolloutActionLimit && !state.IsGameOver && !state.IsTurnEnded)
        {
            List<SimulatedAction> legal = node.LegalActions;
            if (legal == null)
            {
                legal = simulator.GenerateLegalActions(state);
                node.SynchronizeLegalActions(legal, settings.MaxActionsPerNode);
            }
            if (legal.Count == 0) break;
            if (node.TryTakeUnexpanded(out SimulatedAction expansionAction))
            {
                state = simulator.ApplyAction(state, expansionAction, random);
                MCTSNode child = new(node, expansionAction) { PlayerIndex = state.CurrentPlayerIndex };
                node.Children.Add(child);
                node = child;
                path.Add(node);
                actionDepth++;
                break;
            }

            MCTSNode selectedChild = node.SelectChild(legal, settings.ExplorationConstant, rootState.RootPlayerIndex);
            if (selectedChild == null) break;
            SimulatedAction currentAction = FindEquivalent(legal, selectedChild.IncomingAction);
            if (currentAction == null) break;
            state = simulator.ApplyAction(state, currentAction, random);
            node = selectedChild;
            path.Add(node);
            actionDepth++;
        }

        while (actionDepth < settings.RolloutActionLimit && !state.IsGameOver && !state.IsTurnEnded)
        {
            List<SimulatedAction> legal = simulator.GenerateLegalActions(state);
            if (legal.Count == 0) break;
            SimulatedAction rolloutAction = SelectRolloutAction(legal);
            state = simulator.ApplyAction(state, rolloutAction, random);
            actionDepth++;
        }

        double value = HeuristicEvaluator.Evaluate(state, rootState.CurrentPlayerIndex);
        foreach (MCTSNode visited in path) visited.Backpropagate(value);
    }

    private SimulatedAction SelectRolloutAction(List<SimulatedAction> legal)
    {
        int candidateCount = Math.Min(settings.ExpandTopCandidatesBias, legal.Count);
        double min = legal[candidateCount - 1].PriorHeuristic;
        double total = 0;
        for (int i = 0; i < candidateCount; i++) total += Math.Max(0.1, legal[i].PriorHeuristic - min + 1);
        double roll = random.NextDouble() * total;
        for (int i = 0; i < candidateCount; i++)
        {
            roll -= Math.Max(0.1, legal[i].PriorHeuristic - min + 1);
            if (roll <= 0) return legal[i];
        }
        return legal[0];
    }

    private static SimulatedAction FindEquivalent(List<SimulatedAction> legal, SimulatedAction target)
    {
        foreach (SimulatedAction action in legal) if (action.Equals(target)) return action;
        return null;
    }
}
