using System;
using UnityEngine;

public sealed class ResilientAIPlanner : IAIPlanner, IDisposable
{
    private readonly IAIPlanner primary;
    private readonly IAIPlanner fallback;
    private bool warningLogged;

    public ResilientAIPlanner(IAIPlanner primary, IAIPlanner fallback, string initializationError = "")
    {
        this.primary = primary;
        this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        InitializationError = initializationError ?? string.Empty;
    }

    public bool LastSearchUsedFallback { get; private set; }
    public string LastFallbackReason { get; private set; } = string.Empty;
    public string InitializationError { get; }

    public MCTSResult Search(BattleStateSnapshot rootState)
    {
        if (primary == null)
        {
            return SearchFallback(rootState, string.IsNullOrEmpty(InitializationError)
                ? "Neural planner is unavailable."
                : InitializationError);
        }

        try
        {
            MCTSResult result = primary.Search(rootState);
            LastSearchUsedFallback = false;
            LastFallbackReason = string.Empty;
            return result;
        }
        catch (Exception exception)
        {
            return SearchFallback(rootState, exception.Message);
        }
    }

    public void Dispose()
    {
        if (primary is IDisposable primaryDisposable)
        {
            primaryDisposable.Dispose();
        }
        if (fallback is IDisposable fallbackDisposable)
        {
            fallbackDisposable.Dispose();
        }
    }

    private MCTSResult SearchFallback(BattleStateSnapshot rootState, string reason)
    {
        LastSearchUsedFallback = true;
        LastFallbackReason = reason ?? "Unknown neural planner error.";
        if (!warningLogged)
        {
            warningLogged = true;
            Debug.LogWarning($"[AI ML] Falling back to legacy MCTS: {LastFallbackReason}");
        }

        MCTSResult result = fallback.Search(rootState);
        result.UsedFallback = true;
        result.FallbackReason = LastFallbackReason;
        return result;
    }
}
