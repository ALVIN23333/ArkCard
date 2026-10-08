using NUnit.Framework;

public class CardBattleAgentObservationTests
{
    [Test]
    public void BuildObservations_PlaceholderHasSchemaLength()
    {
        float[] features = CardBattleObservationBuilder.Build(null, 0);
        Assert.AreEqual(AIEncodingSchema.StateFeatureCount, features.Length);
        Assert.AreEqual(2180, features.Length);
    }

    [Test]
    public void BuildObservations_EncodedStateHasSchemaLength()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        state.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(11, 0, CardState.Hand, 2, 3, PassiveType.Guard, false));
        float[] features = CardBattleObservationBuilder.Build(state, 0);
        Assert.AreEqual(AIEncodingSchema.StateFeatureCount, features.Length);
        CollectionAssert.AreEqual(AIFeatureEncoder.EncodeState(state, 0), features);
    }
}
