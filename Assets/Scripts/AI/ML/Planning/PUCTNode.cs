using System;
using System.Collections.Generic;

public sealed class PUCTNode
{
    public PUCTNode(PUCTNode parent, SimulatedAction incomingAction, int playerIndex, float prior)
    {
        Parent = parent;
        IncomingAction = incomingAction;
        PlayerIndex = playerIndex;
        Prior = prior;
    }

    public PUCTNode Parent { get; }
    public SimulatedAction IncomingAction { get; }
    public int PlayerIndex { get; }
    public float Prior { get; set; }
    public int VisitCount { get; private set; }
    public double ValueSum { get; private set; }
    public double MeanValue => VisitCount > 0 ? ValueSum / VisitCount : 0;
    public bool Expanded { get; private set; }
    public List<PUCTNode> Children { get; } = new();

    public void Expand(IReadOnlyList<SimulatedAction> actions, IReadOnlyList<float> priors)
    {
        if (actions == null || priors == null || actions.Count != priors.Count)
        {
            throw new ArgumentException("PUCT expansion requires one prior per action.");
        }

        Children.Clear();
        for (int index = 0; index < actions.Count; index++)
        {
            int childPlayerIndex = actions[index].Type == SimulatedActionType.EndTurn
                ? -1
                : PlayerIndex;
            Children.Add(new PUCTNode(this, actions[index], childPlayerIndex, priors[index]));
        }
        Expanded = true;
    }

    public PUCTNode SelectChild(float explorationConstant)
    {
        PUCTNode selected = null;
        double bestScore = double.NegativeInfinity;
        double parentVisits = Math.Sqrt(Math.Max(1, VisitCount));
        foreach (PUCTNode child in Children)
        {
            int childPlayer = child.GetResolvedPlayerIndex();
            if (childPlayer < 0)
            {
                childPlayer = PlayerIndex;
            }
            double exploitation = childPlayer == PlayerIndex ? child.MeanValue : -child.MeanValue;
            double exploration = explorationConstant * child.Prior * parentVisits / (1 + child.VisitCount);
            double score = exploitation + exploration;
            if (score > bestScore)
            {
                bestScore = score;
                selected = child;
            }
        }
        return selected;
    }

    public void ResolvePlayerIndex(int playerIndex)
    {
        if (PlayerIndex < 0)
        {
            resolvedPlayerIndex = playerIndex;
        }
    }

    public int GetResolvedPlayerIndex()
    {
        return PlayerIndex >= 0 ? PlayerIndex : resolvedPlayerIndex;
    }

    public void Backpropagate(double value)
    {
        VisitCount++;
        ValueSum += Math.Max(-1, Math.Min(1, value));
    }

    private int resolvedPlayerIndex = -1;
}
