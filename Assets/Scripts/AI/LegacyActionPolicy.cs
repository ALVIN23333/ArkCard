using System;
using System.Collections.Generic;

/// <summary>
/// Compatibility adapter for the v1 heuristic planner. Legal action generation is intentionally
/// policy-free; only the legacy planner calls this class to restore its historic ordering.
/// </summary>
public static class LegacyActionPolicy
{
    public static List<SimulatedAction> Rank(BattleStateSnapshot state, List<SimulatedAction> legalActions)
    {
        List<SimulatedAction> ranked = legalActions != null
            ? new List<SimulatedAction>(legalActions)
            : new List<SimulatedAction>();

        foreach (SimulatedAction action in ranked)
        {
            action.PriorHeuristic = HeuristicEvaluator.ScoreAction(state, action);
        }

        ranked.Sort(Compare);
        return ranked;
    }

    public static int Compare(SimulatedAction left, SimulatedAction right)
    {
        int prior = right.PriorHeuristic.CompareTo(left.PriorHeuristic);
        if (prior != 0)
        {
            return prior;
        }

        int type = left.Type.CompareTo(right.Type);
        if (type != 0)
        {
            return type;
        }

        int source = left.SourceCardId.CompareTo(right.SourceCardId);
        if (source != 0)
        {
            return source;
        }

        int targetCount = left.Targets.Count.CompareTo(right.Targets.Count);
        if (targetCount != 0)
        {
            return targetCount;
        }

        for (int index = 0; index < left.Targets.Count; index++)
        {
            int kind = left.Targets[index].Kind.CompareTo(right.Targets[index].Kind);
            if (kind != 0)
            {
                return kind;
            }

            int targetId = left.Targets[index].Id.CompareTo(right.Targets[index].Id);
            if (targetId != 0)
            {
                return targetId;
            }
        }

        return 0;
    }
}
