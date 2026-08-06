using System;
using System.Collections.Generic;

public sealed class MCTSNode
{
    private readonly List<SimulatedAction> unexpandedActions = new();

    public MCTSNode(MCTSNode parent, SimulatedAction incomingAction)
    {
        Parent = parent;
        IncomingAction = incomingAction;
    }

    public MCTSNode Parent { get; }
    public SimulatedAction IncomingAction { get; }
    public List<MCTSNode> Children { get; } = new();
    public int PlayerIndex = -1;
    public List<SimulatedAction> LegalActions;
    public int VisitCount { get; private set; }
    public double ValueSum { get; private set; }
    public double MeanValue => VisitCount > 0 ? ValueSum / VisitCount : 0;
    public double PriorHeuristic => IncomingAction != null ? IncomingAction.PriorHeuristic : 0;

    public void SynchronizeLegalActions(List<SimulatedAction> legalActions, int maxActionsPerNode = 10)
    {
        List<SimulatedAction> filtered = new();
        int keptNonEndTurn = 0;
        foreach (SimulatedAction action in legalActions)
        {
            if (action.Type == SimulatedActionType.EndTurn)
            {
                filtered.Add(action);
                continue;
            }
            if (keptNonEndTurn < maxActionsPerNode)
            {
                filtered.Add(action);
                keptNonEndTurn++;
            }
        }
        LegalActions = filtered;
        foreach (SimulatedAction action in filtered)
        {
            if (!HasChild(action) && !Contains(unexpandedActions, action))
            {
                unexpandedActions.Add(action);
            }
        }
        unexpandedActions.RemoveAll(action => !Contains(filtered, action));
        unexpandedActions.Sort((left, right) => right.PriorHeuristic.CompareTo(left.PriorHeuristic));
    }

    public bool TryTakeUnexpanded(out SimulatedAction action)
    {
        if (unexpandedActions.Count == 0)
        {
            action = null;
            return false;
        }
        action = unexpandedActions[0];
        unexpandedActions.RemoveAt(0);
        return true;
    }

    public MCTSNode SelectChild(List<SimulatedAction> legalActions, double explorationConstant, int rootPlayerIndex)
    {
        MCTSNode best = null;
        double bestScore = double.NegativeInfinity;
        double parentLog = Math.Log(Math.Max(1, VisitCount));
        foreach (MCTSNode child in Children)
        {
            if (!Contains(legalActions, child.IncomingAction)) continue;
            double score;
            if (child.VisitCount == 0)
            {
                score = double.PositiveInfinity;
            }
            else
            {
                // The parent node decides: a maximizing parent (+1) or a minimizing parent (-1)
                // selects children by the root-player perspective value.
                double sign = PlayerIndex == rootPlayerIndex ? 1.0 : -1.0;
                score = sign * child.MeanValue + explorationConstant * Math.Sqrt(parentLog / child.VisitCount);
            }
            if (score > bestScore || (Math.Abs(score - bestScore) < 0.000001 && child.PriorHeuristic > (best != null ? best.PriorHeuristic : double.MinValue)))
            {
                best = child;
                bestScore = score;
            }
        }
        return best;
    }

    public void Backpropagate(double value)
    {
        VisitCount++;
        ValueSum += Math.Max(-1, Math.Min(1, value));
    }

    private bool HasChild(SimulatedAction action)
    {
        foreach (MCTSNode child in Children) if (child.IncomingAction.Equals(action)) return true;
        return false;
    }

    private static bool Contains(List<SimulatedAction> actions, SimulatedAction target)
    {
        foreach (SimulatedAction action in actions) if (action.Equals(target)) return true;
        return false;
    }
}
