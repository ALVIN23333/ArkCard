using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 统一效果注册表一致性测试：枚举覆盖、标签对齐、定向真值表、参数校验与旧 schema 等价。
/// </summary>
public class EffectRegistryTests
{
    [Test]
    public void EveryNonNoneEffectType_HasExactlyOneRegisteredDefinition()
    {
        int registeredCount = 0;
        foreach (EffectType effectType in Enum.GetValues(typeof(EffectType)))
        {
            if (effectType == EffectType.None)
            {
                continue;
            }

            Assert.IsTrue(EffectRegistry.IsRegistered(effectType), $"Missing definition for {effectType}");
            registeredCount++;
        }

        int allCount = 0;
        foreach (ICardEffectDefinition _ in EffectRegistry.All)
        {
            allCount++;
        }

        Assert.AreEqual(registeredCount, allCount, "Registry must contain no duplicates or extras.");
    }

    [Test]
    public void Labels_AreAlignedWithEnumOrder()
    {
        IReadOnlyList<string> labels = EffectRegistry.GetLabels();
        Array values = Enum.GetValues(typeof(EffectType));
        Assert.AreEqual(values.Length, labels.Count, "Label count must equal enum value count.");

        int index = 0;
        foreach (EffectType effectType in values)
        {
            Assert.AreEqual(labels[index], EffectRegistry.Get(effectType).Label, $"Label mismatch at index {index}");
            index++;
        }
    }

    [Test]
    public void IsTargeted_MatchesLegacyTruthTable()
    {
        HashSet<EffectType> targeted = new()
        {
            EffectType.DealDamageToEnemy,
            EffectType.BuffEnemy,
            EffectType.SlienceEnemy,
            EffectType.DestoryEnemy,
            EffectType.BuffAlly,
            EffectType.HealAlly,
            EffectType.OtherBackHand,
            EffectType.AllyBackHand,
            EffectType.EnemyBackHand,
            EffectType.ReviveAlly,
            EffectType.Damage,
            EffectType.Heal,
            EffectType.Destroy,
            EffectType.Buff,
            EffectType.BackHand,
            EffectType.Revive,
            EffectType.Silence,
        };

        foreach (EffectType effectType in Enum.GetValues(typeof(EffectType)))
        {
            if (effectType == EffectType.None)
            {
                continue;
            }

            Assert.AreEqual(
                targeted.Contains(effectType),
                EffectRegistry.Get(effectType).IsTargeted,
                $"IsTargeted mismatch for {effectType}");
        }
    }

    [Test]
    public void RequiredParameterValidation_MatchesLegacySchema()
    {
        CardEffectData draw = new() { effectType = EffectType.Draw };
        Assert.IsTrue(EffectRegistry.TryGetMissingRequiredParameter(draw, out EffectValueParameter missing), "Draw requires a count.");
        Assert.AreEqual(0, missing.Index);

        CardEffectData damageAll = new() { effectType = EffectType.DamageAll, effectValues = new[] { 3 } };
        Assert.IsFalse(EffectRegistry.TryGetMissingRequiredParameter(damageAll, out _));

        CardEffectData dealDamage = new() { effectType = EffectType.DealDamageToEnemy, effectValues = new[] { 3 } };
        Assert.IsFalse(EffectRegistry.TryGetMissingRequiredParameter(dealDamage, out _), "Target count is optional.");

        CardEffectData buffEnemy = new() { effectType = EffectType.BuffEnemy, effectValues = new[] { 1, 1, 2 } };
        Assert.IsFalse(EffectRegistry.TryGetMissingRequiredParameter(buffEnemy, out _));

        CardEffectData silence = new() { effectType = EffectType.SlienceEnemy, effectValues = new[] { 0, 1 } };
        Assert.IsFalse(EffectRegistry.TryGetMissingRequiredParameter(silence, out _), "Silence has no required parameters.");

        CardEffectData destroy = new() { effectType = EffectType.DestoryEnemy };
        Assert.IsFalse(EffectRegistry.TryGetMissingRequiredParameter(destroy, out _), "Destroy has no required parameters.");
    }

    [Test]
    public void SelectionCount_UsesBuffIndexTwoAndReviveFieldCap()
    {
        Assert.AreEqual(
            3,
            EffectRegistry.Get(EffectType.BuffEnemy).GetSelectionCount(new CardEffectData { effectValues = new[] { 1, 1, 3 } }));
        Assert.AreEqual(
            2,
            EffectRegistry.Get(EffectType.BuffAlly).GetSelectionCount(new CardEffectData { effectValues = new[] { 1, 1, 2 } }));
        Assert.AreEqual(
            1,
            EffectRegistry.Get(EffectType.DealDamageToEnemy).GetSelectionCount(new CardEffectData { effectValues = new[] { 3 } }));

        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateCard(1, 0, CardState.Field, 1, 1, PassiveType.None, false);
        CardEffectData revive = new() { effectType = EffectType.ReviveAlly, effectValues = new[] { 0, 3 } };

        Assert.AreEqual(
            3,
            EffectRegistry.Get(EffectType.ReviveAlly).GetSimulationSelectionCount(state, source, revive),
            "Revive selection count must cap at open field slots.");

        for (int i = 0; i < GameConst.fieldMax; i++)
        {
            state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(100 + i, 0, CardState.Field, 1, 1, PassiveType.None, false));
        }

        Assert.AreEqual(
            0,
            EffectRegistry.Get(EffectType.ReviveAlly).GetSimulationSelectionCount(state, source, revive),
            "Revive must be a no-op when the field is full.");
    }

    [Test]
    public void UnregisteredEnumValue_FallsBackToNoOpDefinition()
    {
        const EffectType unregistered = (EffectType)999;
        Assert.IsFalse(EffectRegistry.IsRegistered(unregistered));

        ICardEffectDefinition definition = EffectRegistry.Get(unregistered);
        Assert.AreEqual(EffectType.None, definition.EffectType);
        Assert.AreEqual("未定义", definition.Label);
        Assert.AreEqual(0, definition.Parameters.Count);
        Assert.IsFalse(definition.IsTargeted);
        Assert.AreEqual(0, definition.SuggestedArrayLength);
        Assert.IsFalse(EffectRegistry.TryGetMissingRequiredParameter(new CardEffectData { effectType = unregistered }, out _));
    }

    [Test]
    public void HeuristicScores_PreserveLegacyValues()
    {
        Assert.AreEqual(5, EffectRegistry.Get(EffectType.Draw).HeuristicScore(new CardEffectData { effectValues = new[] { 1 } }));
        Assert.AreEqual(14, EffectRegistry.Get(EffectType.DamageAllEnemy).HeuristicScore(new CardEffectData { effectValues = new[] { 2 } }));
        Assert.AreEqual(18, EffectRegistry.Get(EffectType.DestoryEnemy).HeuristicScore(new CardEffectData { effectValues = new int[0] }));
        Assert.AreEqual(9, EffectRegistry.Get(EffectType.SlienceEnemy).HeuristicScore(new CardEffectData()));
        Assert.AreEqual(14, EffectRegistry.Get(EffectType.ReviveAlly).HeuristicScore(new CardEffectData()));
        Assert.AreEqual(10, EffectRegistry.Get(EffectType.EnemyBackHand).HeuristicScore(new CardEffectData()));
        Assert.AreEqual(0, EffectRegistry.Get(EffectType.BuffAllEnemies).HeuristicScore(new CardEffectData { effectValues = new[] { 0 } }));
        Assert.AreEqual(0, EffectRegistry.Get(EffectType.None).HeuristicScore(new CardEffectData()));
    }
}
