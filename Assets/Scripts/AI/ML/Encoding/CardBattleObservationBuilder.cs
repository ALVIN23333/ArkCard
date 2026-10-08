using System;

public static class CardBattleObservationBuilder
{
    public static float[] Build(BattleStateSnapshot state, int playerIndex)
    {
        if (state != null)
        {
            try
            {
                float[] features = AIFeatureEncoder.EncodeState(state, playerIndex);
                if (features != null && features.Length == AIEncodingSchema.StateFeatureCount)
                {
                    return features;
                }
            }
            catch (Exception)
            {
                // Observation size must stay at the schema length even if encoding fails.
            }
        }

        return new float[AIEncodingSchema.StateFeatureCount];
    }
}
