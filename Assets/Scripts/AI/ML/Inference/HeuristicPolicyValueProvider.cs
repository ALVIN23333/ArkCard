using System;
using System.Collections.Generic;

/// <summary>
/// A deterministic policy/value adapter used for teacher data generation and migration tests.
/// It is not used by the released neural planner unless explicitly requested by a tool.
/// </summary>
public sealed class HeuristicPolicyValueProvider : IPolicyValueProvider
{
    public bool IsReady => true;
    public string ModelVersion => "legacy-heuristic-teacher";

    public PolicyValueEvaluation Evaluate(
        BattleStateSnapshot state,
        int observerPlayerIndex,
        IReadOnlyList<SimulatedAction> legalActions)
    {
        if (state == null)
        {
            return PolicyValueEvaluation.Failed("State is null.", ModelVersion);
        }

        float value = (float)HeuristicEvaluator.Evaluate(state, observerPlayerIndex);
        List<float> priors = new();
        if (legalActions != null && legalActions.Count > 0)
        {
            double maximum = double.NegativeInfinity;
            double[] scores = new double[legalActions.Count];
            for (int index = 0; index < legalActions.Count; index++)
            {
                scores[index] = HeuristicEvaluator.ScoreAction(state, legalActions[index]);
                maximum = Math.Max(maximum, scores[index]);
            }
            double total = 0;
            for (int index = 0; index < scores.Length; index++)
            {
                scores[index] = Math.Exp(Math.Max(-50, Math.Min(50, scores[index] - maximum)));
                total += scores[index];
            }
            for (int index = 0; index < scores.Length; index++)
            {
                priors.Add(total > 0 ? (float)(scores[index] / total) : 1f / scores.Length);
            }
        }

        return new PolicyValueEvaluation
        {
            Success = true,
            ModelVersion = ModelVersion,
            Value = Math.Max(-1f, Math.Min(1f, value)),
            Priors = priors,
        };
    }

    public void Dispose()
    {
    }
}
