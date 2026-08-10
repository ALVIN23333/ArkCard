using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

public class ConfigurableEffectTests
{
    [Test]
    public void EffectType_LegacyValuesRemainStableAndNewValuesAreAppended()
    {
        Assert.AreEqual(0, (int)EffectType.None);
        Assert.AreEqual(11, (int)EffectType.DisCard);
        Assert.AreEqual(100, (int)EffectType.DealDamageToEnemy);
        Assert.AreEqual(109, (int)EffectType.ReviveAlly);
        Assert.AreEqual(110, (int)EffectType.Damage);
        Assert.AreEqual(118, (int)EffectType.SummonRandomCostMinion);
        Assert.AreEqual(119, (int)EffectType.DrawCards);
        Assert.AreEqual(120, (int)EffectType.Cost);
        Assert.AreEqual(121, (int)EffectType.Silence);
    }

    [Test]
    public void ConfigurableEffects_RequireSelectionOnlyInSelectedMode()
    {
        ICardEffectDefinition damage = EffectRegistry.Get(EffectType.Damage);
        Assert.IsTrue(damage.RequiresTargetSelection(new CardEffectData { targetMode = EffectTargetMode.Selected }));
        Assert.IsFalse(damage.RequiresTargetSelection(new CardEffectData { targetMode = EffectTargetMode.Random }));
        Assert.IsFalse(damage.RequiresTargetSelection(new CardEffectData { targetMode = EffectTargetMode.All }));
        Assert.IsTrue(EffectRegistry.Get(EffectType.Revive).RequiresTargetSelection(new CardEffectData()));
    }

    [Test]
    public void EditorCatalog_ListsEachUnifiedEffectFamilyOnce()
    {
        Dictionary<EffectType, int> counts = new();
        foreach (EffectEditorOption option in EffectEditorCatalog.GetOptions())
        {
            if (!EffectEditorCatalog.IsUnified(option.EffectType)) continue;
            counts.TryGetValue(option.EffectType, out int count);
            counts[option.EffectType] = count + 1;
        }

        for (EffectType type = EffectType.Damage; type <= EffectType.Silence; type++)
        {
            Assert.AreEqual(1, counts.TryGetValue(type, out int count) ? count : 0, $"{type} must have one editor menu entry.");
        }

        CollectionAssert.Contains(EffectEditorCatalog.GetModes(EffectType.Damage), EffectTargetMode.Selected);
        CollectionAssert.Contains(EffectEditorCatalog.GetModes(EffectType.Destroy), EffectTargetMode.All);
        CollectionAssert.Contains(EffectEditorCatalog.GetModes(EffectType.BackHand), EffectTargetMode.Random);
        CollectionAssert.AreEquivalent(
            new[] { EffectTargetMode.Self, EffectTargetMode.All, EffectTargetMode.Selected, EffectTargetMode.Random },
            EffectEditorCatalog.GetModes(EffectType.Silence));
        foreach (EffectEditorOption option in EffectEditorCatalog.GetOptions())
            Assert.AreEqual(EffectEditorSection.None, option.Section, $"{option.Label} must be a root menu item.");
    }

    [Test]
    public void DrawBoth_DrawsConfiguredCountForEachPlayer()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateSpell(190, 0);
        for (int playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            for (int i = 0; i < 3; i++)
                state.GetPlayer(playerIndex).DeckRemaining.Add(
                    SimulationTestHelpers.CreateCard(191 + playerIndex * 10 + i, playerIndex, CardState.Deck, 1, 1, PassiveType.None, false));
        }
        CardEffectData effect = new()
        {
            effectType = EffectType.DrawCards,
            targetSide = EffectTargetSide.Both,
            effectValues = new[] { 2 },
        };

        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(1));

        Assert.AreEqual(2, state.GetPlayer(0).Hand.Count);
        Assert.AreEqual(2, state.GetPlayer(1).Hand.Count);
        Assert.AreEqual(1, state.GetPlayer(0).DeckRemaining.Count);
        Assert.AreEqual(1, state.GetPlayer(1).DeckRemaining.Count);
    }

    [Test]
    public void Cost_AddsCurrentAndMaxIndependentlyAndCapsMax()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateSpell(195, 0);
        PlayerStateSnapshot player = state.GetPlayer(0);
        player.Cost = 2;
        player.MaxCost = GameConst.costMax - 1;
        CardEffectData effect = new() { effectType = EffectType.Cost, effectValues = new[] { 3, 4 } };

        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(1));

        Assert.AreEqual(5, player.Cost);
        Assert.AreEqual(GameConst.costMax, player.MaxCost);
    }

    [Test]
    public void SilenceRandom_CanHitStealthAndMagicImmuneMinions()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateSpell(196, 0);
        CardStateSnapshot stealth = SimulationTestHelpers.CreateCard(197, 1, CardState.Field, 1, 3, PassiveType.Stealth, false);
        CardStateSnapshot immune = SimulationTestHelpers.CreateCard(198, 1, CardState.Field, 1, 3, PassiveType.MagicImmunity, false);
        state.GetPlayer(1).Field.Add(stealth);
        state.GetPlayer(1).Field.Add(immune);
        CardEffectData effect = new()
        {
            effectType = EffectType.Silence,
            targetSide = EffectTargetSide.Enemy,
            targetMode = EffectTargetMode.Random,
            effectValues = new[] { 2 },
        };

        Assert.AreEqual(0, EffectRegistry.Get(effect.effectType).GetSimulationCandidates(state, source, effect).Count);
        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(1));

        Assert.IsTrue(stealth.IsSilence);
        Assert.IsTrue(immune.IsSilence);
    }

    [Test]
    public void MigrationToSchemaThree_MapsLegacyEffectsRecursivelyAndIsIdempotent()
    {
        CardListSO database = ScriptableObject.CreateInstance<CardListSO>();
        database.effectSchemaVersion = 2;
        CardEffectData nestedCost = new() { effectType = EffectType.AddBothCost, effectValues = new[] { 2 } };
        database.cards.Add(new CardData
        {
            cardType = CardType.Minion,
            effects = new List<CardEffectData>
            {
                new()
                {
                    effectType = EffectType.Draw,
                    effectValues = new[] { 3 },
                    thenEffects = new List<CardEffectData> { nestedCost },
                },
                new() { effectType = EffectType.SlienceEnemy, effectValues = new[] { 0, 3 } },
                new() { effectType = EffectType.AddCostMax, effectValues = new[] { 4 } },
                new() { effectType = EffectType.AddCost, effectValues = new[] { 5 } },
            },
        });

        try
        {
            Assert.IsTrue(CardEffectMigrationService.MigrateIfNeeded(database));
            Assert.AreEqual(3, database.effectSchemaVersion);
            Assert.AreEqual(EffectType.DrawCards, database.cards[0].effects[0].effectType);
            Assert.AreEqual(EffectTargetSide.Friendly, database.cards[0].effects[0].targetSide);
            CollectionAssert.AreEqual(new[] { 3 }, database.cards[0].effects[0].effectValues);
            Assert.AreEqual(EffectType.Cost, database.cards[0].effects[0].thenEffects[0].effectType);
            CollectionAssert.AreEqual(new[] { 2, 2 }, database.cards[0].effects[0].thenEffects[0].effectValues);
            Assert.AreEqual(EffectType.Silence, database.cards[0].effects[1].effectType);
            Assert.AreEqual(EffectTargetMode.Selected, database.cards[0].effects[1].targetMode);
            Assert.AreEqual(EffectTargetSide.Enemy, database.cards[0].effects[1].targetSide);
            CollectionAssert.AreEqual(new[] { 3 }, database.cards[0].effects[1].effectValues);
            Assert.AreEqual(EffectType.Cost, database.cards[0].effects[2].effectType);
            CollectionAssert.AreEqual(new[] { 0, 4 }, database.cards[0].effects[2].effectValues);
            Assert.AreEqual(EffectType.Cost, database.cards[0].effects[3].effectType);
            CollectionAssert.AreEqual(new[] { 5, 0 }, database.cards[0].effects[3].effectValues);
            Assert.IsFalse(CardEffectMigrationService.MigrateIfNeeded(database));
        }
        finally
        {
            Object.DestroyImmediate(database);
        }
    }

    [Test]
    public void DamageBothCharacters_HitsBothHeroesAndBothFields()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateCard(200, 0, CardState.Field, 1, 5, PassiveType.None, false);
        CardStateSnapshot ally = SimulationTestHelpers.CreateCard(201, 0, CardState.Field, 1, 5, PassiveType.None, false);
        CardStateSnapshot enemy = SimulationTestHelpers.CreateCard(202, 1, CardState.Field, 1, 5, PassiveType.None, false);
        state.GetPlayer(0).Field.Add(source);
        state.GetPlayer(0).Field.Add(ally);
        state.GetPlayer(1).Field.Add(enemy);
        CardEffectData effect = new()
        {
            effectType = EffectType.Damage,
            targetSide = EffectTargetSide.Both,
            targetMode = EffectTargetMode.All,
            characterScope = EffectCharacterScope.Characters,
            includeSource = false,
            effectValues = new[] { 2, 1 },
        };

        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(1));

        Assert.AreEqual(28, state.GetPlayer(0).Health);
        Assert.AreEqual(28, state.GetPlayer(1).Health);
        Assert.AreEqual(5, source.Health);
        Assert.AreEqual(3, ally.Health);
        Assert.AreEqual(3, enemy.Health);
    }

    [Test]
    public void RandomHeal_OnlyChoosesInjuredCharacters()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateCard(210, 0, CardState.Field, 1, 5, PassiveType.None, false);
        CardStateSnapshot injured = SimulationTestHelpers.CreateCard(211, 0, CardState.Field, 1, 5, PassiveType.None, false);
        CardStateSnapshot healthy = SimulationTestHelpers.CreateCard(212, 0, CardState.Field, 1, 5, PassiveType.None, false);
        injured.Health = 1;
        state.GetPlayer(0).Field.Add(source);
        state.GetPlayer(0).Field.Add(injured);
        state.GetPlayer(0).Field.Add(healthy);
        CardEffectData effect = new()
        {
            effectType = EffectType.Heal,
            targetSide = EffectTargetSide.Friendly,
            targetMode = EffectTargetMode.Random,
            characterScope = EffectCharacterScope.Minions,
            effectValues = new[] { 3, 2 },
        };

        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(2));

        Assert.AreEqual(4, injured.Health);
        Assert.AreEqual(5, healthy.Health);
        Assert.AreEqual(5, source.Health);
    }

    [Test]
    public void RandomDestroy_CanHitStealthAndMagicImmuneMinions()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateSpell(220, 0);
        CardStateSnapshot stealth = SimulationTestHelpers.CreateCard(221, 1, CardState.Field, 2, 3, PassiveType.Stealth, false);
        CardStateSnapshot immune = SimulationTestHelpers.CreateCard(222, 1, CardState.Field, 2, 3, PassiveType.MagicImmunity, false);
        state.GetPlayer(1).Field.Add(stealth);
        state.GetPlayer(1).Field.Add(immune);
        CardEffectData effect = new()
        {
            effectType = EffectType.Destroy,
            targetSide = EffectTargetSide.Enemy,
            targetMode = EffectTargetMode.Random,
            characterScope = EffectCharacterScope.Minions,
            effectValues = new[] { 2 },
        };

        Assert.AreEqual(0, EffectRegistry.Get(effect.effectType).GetSimulationCandidates(state, source, effect).Count,
            "Manual-selection candidates still exclude stealth and magic immunity.");
        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(3));
        Assert.AreEqual(0, state.GetPlayer(1).Field.Count);
        Assert.AreEqual(2, state.GetPlayer(1).Graveyard.Count);
    }

    [Test]
    public void DestroyAll_UsesConfiguredSidesAndKeepsExcludedSource()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateCard(223, 0, CardState.Field, 1, 5, PassiveType.None, false);
        CardStateSnapshot ally = SimulationTestHelpers.CreateCard(224, 0, CardState.Field, 1, 5, PassiveType.None, false);
        CardStateSnapshot enemy = SimulationTestHelpers.CreateCard(225, 1, CardState.Field, 1, 5, PassiveType.None, false);
        state.GetPlayer(0).Field.Add(source);
        state.GetPlayer(0).Field.Add(ally);
        state.GetPlayer(1).Field.Add(enemy);
        CardEffectData effect = new()
        {
            effectType = EffectType.Destroy,
            targetSide = EffectTargetSide.Both,
            targetMode = EffectTargetMode.All,
            characterScope = EffectCharacterScope.Minions,
            includeSource = false,
        };

        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(3));

        CollectionAssert.AreEqual(new[] { source }, state.GetPlayer(0).Field);
        Assert.AreEqual(0, state.GetPlayer(1).Field.Count);
        Assert.Contains(ally, state.GetPlayer(0).Graveyard);
        Assert.Contains(enemy, state.GetPlayer(1).Graveyard);
    }

    [Test]
    public void RandomBuff_UsesConfiguredUnitCount()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateCard(226, 0, CardState.Field, 1, 5, PassiveType.None, false);
        state.GetPlayer(0).Field.Add(source);
        for (int i = 0; i < 3; i++)
            state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(227 + i, 0, CardState.Field, 1, 5, PassiveType.None, false));
        CardEffectData effect = new()
        {
            effectType = EffectType.Buff,
            targetSide = EffectTargetSide.Friendly,
            targetMode = EffectTargetMode.Random,
            includeSource = false,
            effectValues = new[] { 2, 3, 2 },
        };

        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(4));

        int buffed = 0;
        foreach (CardStateSnapshot card in state.GetPlayer(0).Field)
        {
            if (card.RuntimeId != source.RuntimeId && card.Attack == 3 && card.Health == 8) buffed++;
        }
        Assert.AreEqual(2, buffed);
        Assert.AreEqual(1, source.Attack);
        Assert.AreEqual(5, source.Health);
    }

    [Test]
    public void RandomBackHand_UsesConfiguredUnitCount()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateSpell(240, 0);
        for (int i = 0; i < 3; i++)
            state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(241 + i, 1, CardState.Field, 1, 2, PassiveType.None, false));
        CardEffectData effect = new()
        {
            effectType = EffectType.BackHand,
            targetSide = EffectTargetSide.Enemy,
            targetMode = EffectTargetMode.Random,
            effectValues = new[] { 2 },
        };

        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(5));

        Assert.AreEqual(1, state.GetPlayer(1).Field.Count);
        Assert.AreEqual(2, state.GetPlayer(1).Hand.Count);
    }

    [Test]
    public void DiscardBoth_DiscardsCountForEachPlayer()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateSpell(230, 0);
        for (int i = 0; i < 3; i++)
        {
            state.GetPlayer(0).Hand.Add(SimulationTestHelpers.CreateCard(231 + i, 0, CardState.Hand, 1, 1, PassiveType.None, false));
            state.GetPlayer(1).Hand.Add(SimulationTestHelpers.CreateCard(241 + i, 1, CardState.Hand, 1, 1, PassiveType.None, false));
        }
        CardEffectData effect = new() { effectType = EffectType.Discard, targetSide = EffectTargetSide.Both, effectValues = new[] { 2 } };

        EffectRegistry.Get(effect.effectType).Simulate(state, source, effect, null, new Random(4));

        Assert.AreEqual(1, state.GetPlayer(0).Hand.Count);
        Assert.AreEqual(1, state.GetPlayer(1).Hand.Count);
        Assert.AreEqual(2, state.GetPlayer(0).Graveyard.Count);
        Assert.AreEqual(2, state.GetPlayer(1).Graveyard.Count);
    }

    [Test]
    public void ReviveEnemy_TransfersCardToCasterFieldAndCapsAtOpenSlots()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateCard(250, 0, CardState.Field, 1, 1, PassiveType.None, false);
        state.GetPlayer(0).Field.Add(source);
        for (int i = 0; i < GameConst.fieldMax - 2; i++)
            state.GetPlayer(0).Field.Add(SimulationTestHelpers.CreateCard(251 + i, 0, CardState.Field, 1, 1, PassiveType.None, false));
        CardStateSnapshot deadA = SimulationTestHelpers.CreateCard(260, 1, CardState.Graveyard, 2, 3, PassiveType.None, false);
        CardStateSnapshot deadB = SimulationTestHelpers.CreateCard(261, 1, CardState.Graveyard, 3, 4, PassiveType.None, false);
        state.GetPlayer(1).Graveyard.Add(deadA);
        state.GetPlayer(1).Graveyard.Add(deadB);
        CardEffectData effect = new() { effectType = EffectType.Revive, targetSide = EffectTargetSide.Enemy, effectValues = new[] { 2 } };
        ICardEffectDefinition definition = EffectRegistry.Get(effect.effectType);

        Assert.AreEqual(1, definition.GetSimulationSelectionCount(state, source, effect));
        definition.Simulate(state, source, effect, new List<SimulatedTarget> { SimulatedTarget.Card(deadA.RuntimeId) }, new Random(5));

        Assert.AreEqual(0, deadA.OwnerIndex);
        Assert.Contains(deadA, state.GetPlayer(0).Field);
        Assert.IsFalse(state.GetPlayer(1).Graveyard.Contains(deadA));
        Assert.Contains(deadB, state.GetPlayer(1).Graveyard);
    }

    [Test]
    public void SummonBoth_AddsCountToEachSideAndRespectsCapacity()
    {
        BattleStateSnapshot state = SimulationTestHelpers.CreateBaseState();
        CardStateSnapshot source = SimulationTestHelpers.CreateSpell(270, 0);
        CardData minion = new() { index = 9990, cardType = CardType.Minion, cost = 3, attack = 2, health = 4 };

        SimulationEffectActions.Summon(state, state.GetPlayer(0), minion, 2);
        for (int i = 0; i < GameConst.fieldMax; i++)
            state.GetPlayer(1).Field.Add(SimulationTestHelpers.CreateCard(280 + i, 1, CardState.Field, 1, 1, PassiveType.None, false));
        SimulationEffectActions.Summon(state, state.GetPlayer(1), minion, 2);

        Assert.AreEqual(2, state.GetPlayer(0).Field.Count);
        Assert.AreEqual(GameConst.fieldMax, state.GetPlayer(1).Field.Count);
        Assert.AreEqual(0, state.GetPlayer(0).Field[0].OwnerIndex);
        Assert.AreEqual(3, state.GetPlayer(0).Field[0].Cost);
    }
}
