using System;

public static class AIPlannerFactory
{
    public static IAIPlanner Create(AIModelConfig modelConfig, MCTSSettings legacySettings, int? seed = null)
    {
        MCTSPlanner fallback = new(legacySettings, seed);
        if (modelConfig == null)
        {
            return new ResilientAIPlanner(null, fallback, "AI model config is not assigned.");
        }

        try
        {
            BarracudaPolicyValueProvider provider = new(modelConfig);
            NeuralMCTSPlanner primary = new(provider, modelConfig.CreateSearchSettings(), seed);
            return new ResilientAIPlanner(primary, fallback);
        }
        catch (Exception exception)
        {
            return new ResilientAIPlanner(null, fallback, exception.Message);
        }
    }
}
