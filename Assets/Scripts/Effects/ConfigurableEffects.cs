using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

internal static class ConfigurableEffectUtility
{
    public static List<UnityEngine.Object> ResolveRuntimeTargets(
        CardController source,
        CardEffectData effect,
        List<UnityEngine.Object> selectedTargets,
        bool injuredOnly = false,
        int countIndex = 1)
    {
        if (effect.targetMode == EffectTargetMode.Selected)
        {
            return selectedTargets ?? new List<UnityEngine.Object>();
        }

        if (effect.targetMode == EffectTargetMode.Self)
        {
            return source != null ? new List<UnityEngine.Object> { source } : new List<UnityEngine.Object>();
        }

        List<UnityEngine.Object> candidates = EffectTargetingRules.GetConfiguredCharacters(source, effect, false);
        if (injuredOnly)
        {
            candidates.RemoveAll(target => !IsInjured(target));
        }

        return effect.targetMode == EffectTargetMode.Random
            ? TakeRandom(candidates, GetCount(effect, countIndex))
            : candidates;
    }

    public static List<SimulatedTarget> ResolveSimulationTargets(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        CardEffectData effect,
        List<SimulatedTarget> selectedTargets,
        Random random,
        bool injuredOnly = false,
        int countIndex = 1)
    {
        if (effect.targetMode == EffectTargetMode.Selected)
        {
            return selectedTargets ?? new List<SimulatedTarget>();
        }

        if (effect.targetMode == EffectTargetMode.Self)
        {
            return source != null ? new List<SimulatedTarget> { SimulatedTarget.Card(source.RuntimeId) } : new List<SimulatedTarget>();
        }

        List<SimulatedTarget> candidates = EffectTargetingRules.GetConfiguredCharacters(state, source, effect, false);
        if (injuredOnly)
        {
            candidates.RemoveAll(target => !IsInjured(state, target));
        }

        return effect.targetMode == EffectTargetMode.Random
            ? TakeRandom(candidates, GetCount(effect, countIndex), random)
            : candidates;
    }

    public static int GetCount(CardEffectData effect, int index = 1)
    {
        int count = EffectValues.GetValue(effect, index);
        return count > 0 ? count : 1;
    }

    public static List<UnityEngine.Object> TakeRandom(List<UnityEngine.Object> candidates, int count)
    {
        List<UnityEngine.Object> pool = candidates != null ? new List<UnityEngine.Object>(candidates) : new List<UnityEngine.Object>();
        List<UnityEngine.Object> selected = new();
        int take = Mathf.Min(Mathf.Max(0, count), pool.Count);
        for (int i = 0; i < take; i++)
        {
            int index = UnityEngine.Random.Range(0, pool.Count);
            selected.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return selected;
    }

    public static List<SimulatedTarget> TakeRandom(List<SimulatedTarget> candidates, int count, Random random)
    {
        List<SimulatedTarget> pool = candidates != null ? new List<SimulatedTarget>(candidates) : new List<SimulatedTarget>();
        List<SimulatedTarget> selected = new();
        int take = Math.Min(Math.Max(0, count), pool.Count);
        random ??= new Random();
        for (int i = 0; i < take; i++)
        {
            int index = random.Next(pool.Count);
            selected.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return selected;
    }

    public static List<PlayerController> GetRuntimePlayers(CardController source, EffectTargetSide side)
    {
        List<PlayerController> players = new();
        if (source == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return players;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player != null && EffectTargetingRules.MatchesSide(player == source.player, side))
            {
                players.Add(player);
            }
        }
        return players;
    }

    public static List<PlayerStateSnapshot> GetSimulationPlayers(
        BattleStateSnapshot state,
        CardStateSnapshot source,
        EffectTargetSide side)
    {
        List<PlayerStateSnapshot> players = new();
        if (state == null || source == null)
        {
            return players;
        }

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player != null && EffectTargetingRules.MatchesSide(player.PlayerIndex == source.OwnerIndex, side))
            {
                players.Add(player);
            }
        }
        return players;
    }

    public static CardListSO LoadCardDatabase()
    {
        if (GM.Ins != null && GM.Ins.DM != null && GM.Ins.DM.so != null)
        {
            return GM.Ins.DM.so;
        }
        return Resources.Load<CardListSO>("ArkCardsDatabase");
    }

    public static List<CardData> GetMinionsAtCost(int cost)
    {
        List<CardData> result = new();
        CardListSO database = LoadCardDatabase();
        if (database == null || database.cards == null)
        {
            return result;
        }

        foreach (CardData card in database.cards)
        {
            if (card != null && card.cardType == CardType.Minion && card.cost == cost)
            {
                result.Add(card);
            }
        }
        return result;
    }

    private static bool IsInjured(UnityEngine.Object target)
    {
        return target switch
        {
            CardController card => card.health < card.maxHealth,
            PlayerController player => player.health < player.maxHealth,
            _ => false,
        };
    }

    private static bool IsInjured(BattleStateSnapshot state, SimulatedTarget target)
    {
        if (target.Kind == SimulatedTargetKind.Player)
        {
            PlayerStateSnapshot player = state.GetPlayer(target.Id);
            return player != null && player.Health < player.MaxHealth;
        }

        CardStateSnapshot card = state.FindCard(target.Id);
        return card != null && card.Health < card.MaxHealth;
    }
}

[CardEffect(EffectType.Damage, "伤害")]
public sealed class ConfigurableDamageEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.Damage;
    public override string Label => "伤害";
    public override bool IsTargeted => true;
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "伤害值", 1, true),
        new EffectValueParameter(1, "单位数", 1, false),
    };

    public override bool RequiresTargetSelection(CardEffectData effect) => effect != null && effect.targetMode == EffectTargetMode.Selected;

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
        => EffectTargetingRules.GetConfiguredCharacters(source, effect, true);

    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
        => EffectTargetingRules.GetConfiguredCharacters(state, source, effect, true);

    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        RuntimeEffectActions.DamageTargets(source, ConfigurableEffectUtility.ResolveRuntimeTargets(source, effect, targets), EffectValues.GetValue(effect, 0));
        onComplete?.Invoke();
    }

    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        int damage = EffectValues.GetValue(effect, 0);
        foreach (SimulatedTarget target in ConfigurableEffectUtility.ResolveSimulationTargets(state, source, effect, targets, random))
        {
            if (target.Kind == SimulatedTargetKind.Player) EffectSimulationResolver.DamagePlayer(state, source, state.GetPlayer(target.Id), damage);
            else EffectSimulationResolver.DamageCard(state, source, state.FindCard(target.Id), damage, random);
        }
    }

    public override double ScoreSimulationTarget(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, SimulatedTarget target)
    {
        if (target.Kind == SimulatedTargetKind.Player)
        {
            PlayerStateSnapshot player = state.GetPlayer(target.Id);
            return player == null ? double.MinValue : (EffectValues.GetValue(effect, 0) >= player.Health ? 1000 : 20 - player.Health);
        }
        CardStateSnapshot card = state.FindCard(target.Id);
        return card == null ? double.MinValue : EffectTargetingRules.GetSimulationThreat(card);
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (target is PlayerController player) return EffectValues.GetValue(effect, 0) >= player.health ? 1000 : 20 - player.health;
        return EffectTargetingRules.GetRuntimeThreat(target as CardController);
    }

    public override double HeuristicScore(CardEffectData effect) => EffectValues.GetValue(effect, 0) * 4;
}

[CardEffect(EffectType.Heal, "治疗")]
public sealed class ConfigurableHealEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.Heal;
    public override string Label => "治疗";
    public override bool IsTargeted => true;
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "治疗值", 1, true),
        new EffectValueParameter(1, "单位数", 1, false),
    };

    public override bool RequiresTargetSelection(CardEffectData effect) => effect != null && effect.targetMode == EffectTargetMode.Selected;
    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
        => EffectTargetingRules.GetConfiguredCharacters(source, effect, true);
    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
        => EffectTargetingRules.GetConfiguredCharacters(state, source, effect, true);

    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        RuntimeEffectActions.HealTargets(ConfigurableEffectUtility.ResolveRuntimeTargets(source, effect, targets, effect.targetMode == EffectTargetMode.Random), EffectValues.GetValue(effect, 0));
        onComplete?.Invoke();
    }

    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        int amount = EffectValues.GetValue(effect, 0);
        foreach (SimulatedTarget target in ConfigurableEffectUtility.ResolveSimulationTargets(state, source, effect, targets, random, effect.targetMode == EffectTargetMode.Random))
        {
            if (target.Kind == SimulatedTargetKind.Player) SimulationEffectActions.HealPlayer(state.GetPlayer(target.Id), amount);
            else SimulationEffectActions.HealCard(state.FindCard(target.Id), amount);
        }
    }

    public override double ScoreSimulationTarget(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, SimulatedTarget target)
    {
        if (target.Kind == SimulatedTargetKind.Player)
        {
            PlayerStateSnapshot player = state.GetPlayer(target.Id);
            return player == null ? double.MinValue : Math.Max(0, player.MaxHealth - player.Health) * 3;
        }
        CardStateSnapshot card = state.FindCard(target.Id);
        return card == null ? double.MinValue : Math.Max(0, card.MaxHealth - card.Health) * 3 + EffectTargetingRules.GetSimulationAllyValue(card);
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (target is PlayerController player) return Math.Max(0, player.maxHealth - player.health) * 3;
        if (target is CardController card) return Math.Max(0, card.maxHealth - card.health) * 3 + EffectTargetingRules.GetRuntimeAllyValue(card);
        return double.MinValue;
    }

    public override double HeuristicScore(CardEffectData effect) => EffectValues.GetValue(effect, 0) * 1.5;
}

[CardEffect(EffectType.Destroy, "消灭")]
public sealed class ConfigurableDestroyEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.Destroy;
    public override string Label => "消灭";
    public override bool IsTargeted => true;
    public override int SelectionCountIndex => 0;
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[] { new EffectValueParameter(0, "单位数", 1, true) };
    public override bool RequiresTargetSelection(CardEffectData effect) => effect != null && effect.targetMode == EffectTargetMode.Selected;

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        List<UnityEngine.Object> targets = EffectTargetingRules.GetConfiguredCharacters(source, effect, true);
        targets.RemoveAll(target => target is not CardController);
        return targets;
    }

    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        List<SimulatedTarget> targets = EffectTargetingRules.GetConfiguredCharacters(state, source, effect, true);
        targets.RemoveAll(target => target.Kind != SimulatedTargetKind.Card);
        return targets;
    }

    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        List<UnityEngine.Object> resolved = ConfigurableEffectUtility.ResolveRuntimeTargets(source, effect, targets, false, 0);
        resolved.RemoveAll(target => target is not CardController);
        RuntimeEffectActions.DestroyTargets(resolved);
        onComplete?.Invoke();
    }

    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        List<SimulatedTarget> resolved = ConfigurableEffectUtility.ResolveSimulationTargets(state, source, effect, targets, random, false, 0);
        resolved.RemoveAll(target => target.Kind != SimulatedTargetKind.Card);
        foreach (SimulatedTarget target in resolved) EffectSimulationResolver.KillCard(state, state.FindCard(target.Id), random);
    }

    public override double HeuristicScore(CardEffectData effect) => ConfigurableEffectUtility.GetCount(effect, 0) * 18;
}

[CardEffect(EffectType.Buff, "强化")]
public sealed class ConfigurableBuffEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.Buff;
    public override string Label => "强化";
    public override bool IsTargeted => true;
    public override int SelectionCountIndex => 2;
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "攻击变化", 0, true),
        new EffectValueParameter(1, "生命变化", 0, true),
        new EffectValueParameter(2, "单位数", 1, false),
    };
    public override bool RequiresTargetSelection(CardEffectData effect) => effect != null && effect.targetMode == EffectTargetMode.Selected;

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        List<UnityEngine.Object> targets = EffectTargetingRules.GetConfiguredCharacters(source, effect, true);
        targets.RemoveAll(target => target is not CardController);
        return targets;
    }
    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        List<SimulatedTarget> targets = EffectTargetingRules.GetConfiguredCharacters(state, source, effect, true);
        targets.RemoveAll(target => target.Kind != SimulatedTargetKind.Card);
        return targets;
    }
    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        List<UnityEngine.Object> resolved = ConfigurableEffectUtility.ResolveRuntimeTargets(source, effect, targets, false, 2);
        resolved.RemoveAll(target => target is not CardController);
        RuntimeEffectActions.BuffTargets(resolved, EffectValues.GetValue(effect, 0), EffectValues.GetValue(effect, 1));
        onComplete?.Invoke();
    }
    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        List<SimulatedTarget> resolved = ConfigurableEffectUtility.ResolveSimulationTargets(state, source, effect, targets, random, false, 2);
        resolved.RemoveAll(target => target.Kind != SimulatedTargetKind.Card);
        foreach (SimulatedTarget target in resolved)
            SimulationEffectActions.AddStats(state.FindCard(target.Id), EffectValues.GetValue(effect, 0), EffectValues.GetValue(effect, 1));
    }
    public override double HeuristicScore(CardEffectData effect) => EffectValues.GetValue(effect, 0) * 2 + EffectValues.GetValue(effect, 1);
}

[CardEffect(EffectType.BackHand, "回手")]
public sealed class ConfigurableBackHandEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.BackHand;
    public override string Label => "回手";
    public override bool IsTargeted => true;
    public override int SelectionCountIndex => 0;
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[] { new EffectValueParameter(0, "单位数", 1, true) };
    public override bool RequiresTargetSelection(CardEffectData effect) => effect != null && effect.targetMode == EffectTargetMode.Selected;
    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        List<UnityEngine.Object> targets = EffectTargetingRules.GetConfiguredCharacters(source, effect, true);
        targets.RemoveAll(target => target is not CardController);
        return targets;
    }
    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        List<SimulatedTarget> targets = EffectTargetingRules.GetConfiguredCharacters(state, source, effect, true);
        targets.RemoveAll(target => target.Kind != SimulatedTargetKind.Card);
        return targets;
    }
    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        List<UnityEngine.Object> resolved = ConfigurableEffectUtility.ResolveRuntimeTargets(source, effect, targets, false, 0);
        resolved.RemoveAll(target => target is not CardController);
        RuntimeEffectActions.ReturnTargetsToOwnerHand(resolved);
        onComplete?.Invoke();
    }
    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        List<SimulatedTarget> resolved = ConfigurableEffectUtility.ResolveSimulationTargets(state, source, effect, targets, random, false, 0);
        resolved.RemoveAll(target => target.Kind != SimulatedTargetKind.Card);
        foreach (SimulatedTarget target in resolved) SimulationEffectActions.ReturnToHand(state, state.FindCard(target.Id), random);
    }
}

[CardEffect(EffectType.Discard, "弃牌")]
public sealed class ConfigurableDiscardEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.Discard;
    public override string Label => "弃牌";
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[] { new EffectValueParameter(0, "弃牌数", 1, true) };
    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        foreach (PlayerController player in ConfigurableEffectUtility.GetRuntimePlayers(source, effect.targetSide)) RuntimeEffectActions.DiscardRandomCards(player, EffectValues.GetValue(effect, 0));
        onComplete?.Invoke();
    }
    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        foreach (PlayerStateSnapshot player in ConfigurableEffectUtility.GetSimulationPlayers(state, source, effect.targetSide)) SimulationEffectActions.Discard(player, EffectValues.GetValue(effect, 0), random);
    }
}

[CardEffect(EffectType.Revive, "复活")]
public sealed class ConfigurableReviveEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.Revive;
    public override string Label => "复活";
    public override bool IsTargeted => true;
    public override TargetSelectionZone SelectionZone => TargetSelectionZone.Graveyard;
    public override int SelectionCountIndex => 0;
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[] { new EffectValueParameter(0, "复活数量", 1, true) };
    public override int GetRuntimeSelectionCount(CardController source, CardEffectData effect)
        => source == null || source.player == null || source.player.fieldController == null ? 0 : Math.Min(GetSelectionCount(effect), Math.Max(0, GameConst.fieldMax - source.player.fieldController.fieldCards.Count));
    public override int GetSimulationSelectionCount(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        PlayerStateSnapshot owner = state != null && source != null ? state.GetPlayer(source.OwnerIndex) : null;
        return owner == null ? 0 : Math.Min(GetSelectionCount(effect), Math.Max(0, GameConst.fieldMax - owner.Field.Count));
    }
    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect) => EffectTargetingRules.GetConfiguredGraveyardMinions(source, effect.targetSide);
    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect) => EffectTargetingRules.GetConfiguredGraveyardMinions(state, source, effect.targetSide);
    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        RuntimeEffectActions.ReviveForController(source != null ? source.player : null, targets);
        onComplete?.Invoke();
    }
    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        PlayerStateSnapshot destination = state.GetPlayer(source.OwnerIndex);
        if (targets != null) foreach (SimulatedTarget target in targets) SimulationEffectActions.ReviveForController(state, destination, state.FindCard(target.Id));
    }
    public override double HeuristicScore(CardEffectData effect) => ConfigurableEffectUtility.GetCount(effect, 0) * 14;
}

[CardEffect(EffectType.SummonMinion, "召唤指定随从")]
public sealed class SummonMinionEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.SummonMinion;
    public override string Label => "召唤指定随从";
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "随从卡 ID", 0, true),
        new EffectValueParameter(1, "召唤数量", 1, true),
    };
    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        CardData data = ConfigurableEffectUtility.LoadCardDatabase()?.GetData(EffectValues.GetValue(effect, 0));
        foreach (PlayerController player in ConfigurableEffectUtility.GetRuntimePlayers(source, effect.targetSide)) RuntimeEffectActions.Summon(player, data, EffectValues.GetValue(effect, 1));
        onComplete?.Invoke();
    }
    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        CardData data = ConfigurableEffectUtility.LoadCardDatabase()?.GetData(EffectValues.GetValue(effect, 0));
        foreach (PlayerStateSnapshot player in ConfigurableEffectUtility.GetSimulationPlayers(state, source, effect.targetSide)) SimulationEffectActions.Summon(state, player, data, EffectValues.GetValue(effect, 1));
    }
}

[CardEffect(EffectType.SummonRandomCostMinion, "召唤随机费用随从")]
public sealed class SummonRandomCostMinionEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.SummonRandomCostMinion;
    public override string Label => "召唤随机费用随从";
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "指定费用", 0, true),
        new EffectValueParameter(1, "召唤数量", 1, true),
    };
    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        List<CardData> pool = ConfigurableEffectUtility.GetMinionsAtCost(EffectValues.GetValue(effect, 0));
        foreach (PlayerController player in ConfigurableEffectUtility.GetRuntimePlayers(source, effect.targetSide))
        {
            int count = EffectValues.GetValue(effect, 1);
            for (int i = 0; i < count && pool.Count > 0; i++) RuntimeEffectActions.Summon(player, pool[UnityEngine.Random.Range(0, pool.Count)], 1);
        }
        onComplete?.Invoke();
    }
    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        List<CardData> pool = ConfigurableEffectUtility.GetMinionsAtCost(EffectValues.GetValue(effect, 0));
        random ??= new Random();
        foreach (PlayerStateSnapshot player in ConfigurableEffectUtility.GetSimulationPlayers(state, source, effect.targetSide))
        {
            int count = EffectValues.GetValue(effect, 1);
            for (int i = 0; i < count && pool.Count > 0; i++) SimulationEffectActions.Summon(state, player, pool[random.Next(pool.Count)], 1);
        }
    }
}

[CardEffect(EffectType.DrawCards, "抽牌")]
public sealed class ConfigurableDrawEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.DrawCards;
    public override string Label => "抽牌";
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "抽牌数量", 1, true),
    };

    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        List<PlayerController> players = ConfigurableEffectUtility.GetRuntimePlayers(source, effect.targetSide);
        if (players.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int remaining = players.Count;
        foreach (PlayerController player in players)
        {
            RuntimeEffectActions.Draw(player, EffectValues.GetValue(effect, 0), () =>
            {
                remaining--;
                if (remaining == 0)
                {
                    onComplete?.Invoke();
                }
            });
        }
    }

    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        foreach (PlayerStateSnapshot player in ConfigurableEffectUtility.GetSimulationPlayers(state, source, effect.targetSide))
            SimulationEffectActions.DrawCards(player, EffectValues.GetValue(effect, 0), random);
    }

    public override double HeuristicScore(CardEffectData effect) => EffectValues.GetValue(effect, 0) * 5;
}

[CardEffect(EffectType.Cost, "费用")]
public sealed class ConfigurableCostEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.Cost;
    public override string Label => "费用";
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "当前费用增加", 1, true),
        new EffectValueParameter(1, "费用上限增加", 0, true),
    };

    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        PlayerController player = source != null ? source.player : null;
        if (player != null)
        {
            int maxCostIncrease = Math.Max(0, EffectValues.GetValue(effect, 1));
            int currentCostIncrease = Math.Max(0, EffectValues.GetValue(effect, 0));
            player.AddMaxCost(maxCostIncrease);
            player.AddCost(currentCostIncrease);
        }
        onComplete?.Invoke();
    }

    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        PlayerStateSnapshot player = state != null && source != null ? state.GetPlayer(source.OwnerIndex) : null;
        if (player == null) return;
        player.MaxCost = Math.Min(GameConst.costMax, player.MaxCost + Math.Max(0, EffectValues.GetValue(effect, 1)));
        player.Cost += Math.Max(0, EffectValues.GetValue(effect, 0));
    }

    public override double HeuristicScore(CardEffectData effect)
        => EffectValues.GetValue(effect, 0) * 2 + EffectValues.GetValue(effect, 1) * 4;
}

[CardEffect(EffectType.Silence, "沉默")]
public sealed class ConfigurableSilenceEffect : CardEffectDefinitionBase
{
    public override EffectType EffectType => EffectType.Silence;
    public override string Label => "沉默";
    public override bool IsTargeted => true;
    public override int SelectionCountIndex => 0;
    public override IReadOnlyList<EffectValueParameter> Parameters { get; } = new[]
    {
        new EffectValueParameter(0, "单位数", 1, true),
    };

    public override bool RequiresTargetSelection(CardEffectData effect)
        => effect != null && effect.targetMode == EffectTargetMode.Selected;

    public override List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect)
    {
        List<UnityEngine.Object> candidates = EffectTargetingRules.GetConfiguredCharacters(source, effect, true);
        candidates.RemoveAll(target => target is not CardController);
        return candidates;
    }

    public override List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect)
    {
        List<SimulatedTarget> candidates = EffectTargetingRules.GetConfiguredCharacters(state, source, effect, true);
        candidates.RemoveAll(target => target.Kind != SimulatedTargetKind.Card);
        return candidates;
    }

    public override void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete)
    {
        List<UnityEngine.Object> resolved = ConfigurableEffectUtility.ResolveRuntimeTargets(source, effect, targets, false, 0);
        resolved.RemoveAll(target => target is not CardController card
            || (effect.targetMode == EffectTargetMode.Self && card.cardData != null && card.cardData.cardType != CardType.Minion));
        RuntimeEffectActions.SilenceTargets(resolved);
        onComplete?.Invoke();
    }

    public override void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random)
    {
        List<SimulatedTarget> resolved = ConfigurableEffectUtility.ResolveSimulationTargets(state, source, effect, targets, random, false, 0);
        resolved.RemoveAll(target => target.Kind != SimulatedTargetKind.Card);
        foreach (SimulatedTarget target in resolved)
        {
            CardStateSnapshot card = state.FindCard(target.Id);
            if (card != null && card.Data != null && card.Data.cardType == CardType.Minion) card.IsSilence = true;
        }
    }

    public override double ScoreSimulationTarget(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, SimulatedTarget target)
    {
        CardStateSnapshot card = state.FindCard(target.Id);
        return card == null ? double.MinValue : EffectTargetingRules.GetSimulationThreat(card)
            + EffectTargetingRules.GetSimulationPassiveBonus(card) * 2
            + EffectTargetingRules.GetSimulationBuffAmount(card);
    }

    public override double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target)
    {
        if (target is not CardController card || card.cardData == null) return double.MinValue;
        return EffectTargetingRules.GetRuntimeThreat(card)
            + EffectTargetingRules.GetRuntimePassiveBonus(card) * 2
            + EffectTargetingRules.GetRuntimeBuffAmount(card);
    }

    public override double HeuristicScore(CardEffectData effect) => ConfigurableEffectUtility.GetCount(effect, 0) * 9;
}
