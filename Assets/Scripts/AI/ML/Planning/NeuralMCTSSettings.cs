using System;

[Serializable]
public sealed class NeuralMCTSSettings
{
    public int Iterations = 192;
    public int TimeBudgetMs = 50;
    public float ExplorationConstant = 1.5f;
    public int DeterminizationCount = 4;
    public int MaxRootTurns = 2;
    public int MaxSearchDepth = 32;
    public bool AddRootExplorationNoise;
    public float RootDirichletAlpha = 0.3f;
    public float RootNoiseFraction = 0.25f;

    public void Clamp()
    {
        Iterations = Math.Max(1, Math.Min(10000, Iterations));
        TimeBudgetMs = Math.Max(1, TimeBudgetMs);
        ExplorationConstant = Math.Max(0.01f, ExplorationConstant);
        DeterminizationCount = Math.Max(1, Math.Min(64, DeterminizationCount));
        MaxRootTurns = Math.Max(1, Math.Min(3, MaxRootTurns));
        MaxSearchDepth = Math.Max(1, Math.Min(256, MaxSearchDepth));
        RootDirichletAlpha = Math.Max(0.01f, RootDirichletAlpha);
        RootNoiseFraction = Math.Max(0f, Math.Min(1f, RootNoiseFraction));
    }
}
