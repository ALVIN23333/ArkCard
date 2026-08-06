using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 新增 ConditionType（HasDiedEnemy / HasNonMagicalImmunity*）在 AI 模拟中的行为断言。
/// </summary>
public class ConditionTypeTests
{
    private static BattleStateSnapshot CreateState()
    {
        return SimulationTestHelpers.CreateBaseState();
    }

    private static CardStateSnapshot CreateSource()
    {
        return SimulationTestHelpers.CreateCard(1, 0, CardState.Field, 1, 1, PassiveType.None, false);
    }

    [Test]
    public void HasDiedEnemy_ChecksEnemyGraveyard()
    {
        BattleStateSnapshot state = CreateState();
        CardStateSnapshot source = CreateSource();
        List<ConditionType> condition = new() { ConditionType.HasDiedEnemy };

        Assert.IsFalse(BattleStateSimulator.CheckConditions(state, source, condition), "Empty enemy graveyard must fail HasDiedEnemy.");

        state.GetPlayer(1).Graveyard.Add(SimulationTestHelpers.CreateCard(2, 1, CardState.Graveyard, 1, 1, PassiveType.None, false));
        Assert.IsTrue(BattleStateSimulator.CheckConditions(state, source, condition), "Non-empty enemy graveyard must pass HasDiedEnemy.");
    }

    [Test]
    public void HasNonMagicalImmunityAlly_IgnoresMagicImmuneAllies()
    {
        BattleStateSnapshot state = CreateState();
        CardStateSnapshot source = CreateSource();
        List<ConditionType> condition = new() { ConditionType.HasNonMagicalImmunityAlly };

        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(2, 0, CardState.Field, 1, 1, PassiveType.MagicImmunity, false));
        Assert.IsFalse(BattleStateSimulator.CheckConditions(state, source, condition), "Magic-immune ally must not satisfy the condition.");

        state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(3, 0, CardState.Field, 1, 1, PassiveType.None, false));
        Assert.IsTrue(BattleStateSimulator.CheckConditions(state, source, condition), "Non-magic-immune ally must satisfy the condition.");
    }

    [Test]
    public void HasNonMagicalImmunityEnemy_ChecksEnemyField()
    {
        BattleStateSnapshot state = CreateState();
        CardStateSnapshot source = CreateSource();
        List<ConditionType> condition = new() { ConditionType.HasNonMagicalImmunityEnemy };

        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(2, 1, CardState.Field, 1, 1, PassiveType.MagicImmunity, false));
        Assert.IsFalse(BattleStateSimulator.CheckConditions(state, source, condition), "Magic-immune enemy must not satisfy the condition.");

        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(3, 1, CardState.Field, 1, 1, PassiveType.None, false));
        Assert.IsTrue(BattleStateSimulator.CheckConditions(state, source, condition), "Non-magic-immune enemy must satisfy the condition.");
    }

    [Test]
    public void HasNonMagicalImmunityOther_ExcludesSource()
    {
        BattleStateSnapshot state = CreateState();
        CardStateSnapshot source = CreateSource();
        state.GetPlayer(0).Field.Add(source);
        List<ConditionType> condition = new() { ConditionType.HasNonMagicalImmunityOther };

        Assert.IsFalse(
            BattleStateSimulator.CheckConditions(state, source, condition),
            "Source itself must not satisfy the other-minion condition.");

        state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(2, 1, CardState.Field, 1, 1, PassiveType.None, false));
        Assert.IsTrue(
            BattleStateSimulator.CheckConditions(state, source, condition),
            "Another non-magic-immune minion must satisfy the condition.");
    }
}
