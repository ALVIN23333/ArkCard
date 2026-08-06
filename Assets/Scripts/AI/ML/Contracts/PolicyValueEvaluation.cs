using System;
using System.Collections.Generic;

public sealed class PolicyValueEvaluation
{
    public bool Success;
    public string Error = string.Empty;
    public string ModelVersion = string.Empty;
    public float Value;
    public List<float> Priors = new();

    public static PolicyValueEvaluation Failed(string error, string modelVersion = "")
    {
        return new PolicyValueEvaluation
        {
            Success = false,
            Error = error ?? "Unknown policy/value provider failure.",
            ModelVersion = modelVersion ?? string.Empty,
        };
    }
}

public interface IPolicyValueProvider : IDisposable
{
    bool IsReady { get; }
    string ModelVersion { get; }

    PolicyValueEvaluation Evaluate(
        BattleStateSnapshot state,
        int observerPlayerIndex,
        IReadOnlyList<SimulatedAction> legalActions);
}

public sealed class AIModelUnavailableException : InvalidOperationException
{
    public AIModelUnavailableException(string message) : base(message)
    {
    }

    public AIModelUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
