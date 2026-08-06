using System;
using System.Collections.Generic;

public static class AIFeatureEncoder
{
    private static readonly CardType[] CardTypes = (CardType[])Enum.GetValues(typeof(CardType));
    private static readonly CardState[] CardStates = (CardState[])Enum.GetValues(typeof(CardState));
    private static readonly PassiveType[] PassiveTypes = (PassiveType[])Enum.GetValues(typeof(PassiveType));
    private static readonly TriggerType[] TriggerTypes = (TriggerType[])Enum.GetValues(typeof(TriggerType));
    private static readonly ConditionType[] ConditionTypes = (ConditionType[])Enum.GetValues(typeof(ConditionType));
    private static readonly EffectType[] EffectTypes = (EffectType[])Enum.GetValues(typeof(EffectType));

    public static float[] EncodeState(BattleStateSnapshot state, int observerPlayerIndex)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (!AIEncodingSchema.IsRuntimeCompatible(out string schemaError))
        {
            throw new InvalidOperationException(schemaError);
        }

        PlayerStateSnapshot observer = state.GetPlayer(observerPlayerIndex);
        if (observer == null)
        {
            throw new ArgumentOutOfRangeException(nameof(observerPlayerIndex), "Observer is not present in the battle snapshot.");
        }

        PlayerStateSnapshot opponent = null;
        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player != null && player.PlayerIndex != observerPlayerIndex)
            {
                opponent = player;
                break;
            }
        }

        float[] features = new float[AIEncodingSchema.StateFeatureCount];
        int offset = 0;
        features[offset++] = state.CurrentPlayerIndex == observerPlayerIndex ? 1f : 0f;
        features[offset++] = state.IsGameOver ? 1f : 0f;
        features[offset++] = state.IsTurnEnded ? 1f : 0f;
        features[offset++] = Normalize(state.RootEndTurnCount, Math.Max(1, state.MaxRootTurns));
        WritePlayerScalars(observer, observerPlayerIndex, features, ref offset);
        WritePlayerScalars(opponent, observerPlayerIndex, features, ref offset);

        WriteCardSlots(observer.Hand, AIEncodingSchema.MaxHandCards, features, ref offset);
        WriteCardSlots(observer.Field, AIEncodingSchema.MaxFieldCards, features, ref offset);
        WriteCardSlots(opponent != null ? opponent.Field : null, AIEncodingSchema.MaxFieldCards, features, ref offset);

        WriteCardSummary(observer.Graveyard, features, ref offset);
        WriteCardSummary(observer.DeckRemaining, features, ref offset);
        WriteCardSummary(opponent != null ? opponent.Graveyard : null, features, ref offset);
        WriteHiddenPoolSummary(opponent, features, ref offset);

        EnsureOffset(offset, AIEncodingSchema.StateFeatureCount, "state");
        return features;
    }

    public static float[] EncodeAction(BattleStateSnapshot state, int observerPlayerIndex, SimulatedAction action)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        float[] features = new float[AIEncodingSchema.ActionFeatureCount];
        int offset = 0;
        WriteOneHot(features, ref offset, (int)action.Type, AIEncodingSchema.ActionTypeCount);

        CardStateSnapshot source = action.SourceCardId != 0 ? state.FindCard(action.SourceCardId) : null;
        CardLocation sourceLocation = FindCardLocation(state, source);
        WriteOneHot(features, ref offset, sourceLocation.ZoneIndex, AIEncodingSchema.ZoneTypeCount);
        features[offset++] = sourceLocation.NormalizedIndex;
        WriteCard(source != null ? source.Data : null, source, features, ref offset);

        features[offset++] = Normalize(action.Targets.Count, AIEncodingSchema.MaxActionTargets);
        for (int index = 0; index < AIEncodingSchema.MaxActionTargets; index++)
        {
            WriteTarget(state, observerPlayerIndex, index < action.Targets.Count ? action.Targets[index] : null, features, ref offset);
        }

        EnsureOffset(offset, AIEncodingSchema.ActionFeatureCount, "action");
        return features;
    }

    public static float[] CombinePolicyInput(float[] stateFeatures, float[] actionFeatures)
    {
        if (stateFeatures == null || stateFeatures.Length != AIEncodingSchema.StateFeatureCount)
        {
            throw new ArgumentException("Unexpected state feature length.", nameof(stateFeatures));
        }
        if (actionFeatures == null || actionFeatures.Length != AIEncodingSchema.ActionFeatureCount)
        {
            throw new ArgumentException("Unexpected action feature length.", nameof(actionFeatures));
        }

        float[] combined = new float[AIEncodingSchema.PolicyInputFeatureCount];
        Array.Copy(stateFeatures, 0, combined, 0, stateFeatures.Length);
        Array.Copy(actionFeatures, 0, combined, stateFeatures.Length, actionFeatures.Length);
        return combined;
    }

    private static void WritePlayerScalars(
        PlayerStateSnapshot player,
        int observerPlayerIndex,
        float[] destination,
        ref int offset)
    {
        if (player == null)
        {
            offset += 8;
            return;
        }

        destination[offset++] = Normalize(player.Health, GameConst.initalHealth);
        destination[offset++] = Normalize(player.MaxHealth, GameConst.initalHealth);
        destination[offset++] = Normalize(player.Cost, GameConst.costMax);
        destination[offset++] = Normalize(player.MaxCost, GameConst.costMax);
        destination[offset++] = Normalize(GetVisibleHandCount(player, observerPlayerIndex), GameConst.handMax);
        destination[offset++] = Normalize(player.Field.Count, GameConst.fieldMax);
        destination[offset++] = Normalize(player.Graveyard.Count, GameConst.librarymax);
        destination[offset++] = Normalize(GetVisibleDeckCount(player, observerPlayerIndex), GameConst.librarymax);
    }

    private static int GetVisibleHandCount(PlayerStateSnapshot player, int observerPlayerIndex)
    {
        if (player.PlayerIndex == observerPlayerIndex || !player.HandIsHidden)
        {
            return player.Hand.Count;
        }
        return player.HiddenHandCount;
    }

    private static int GetVisibleDeckCount(PlayerStateSnapshot player, int observerPlayerIndex)
    {
        return player.PlayerIndex == observerPlayerIndex || player.HiddenDeckCount <= 0
            ? player.DeckRemaining.Count
            : player.HiddenDeckCount;
    }

    private static void WriteCardSlots(
        List<CardStateSnapshot> cards,
        int slotCount,
        float[] destination,
        ref int offset)
    {
        for (int index = 0; index < slotCount; index++)
        {
            CardStateSnapshot card = cards != null && index < cards.Count ? cards[index] : null;
            WriteCard(card != null ? card.Data : null, card, destination, ref offset);
        }
    }

    private static void WriteCardSummary(
        List<CardStateSnapshot> cards,
        float[] destination,
        ref int offset)
    {
        int start = offset;
        if (cards == null || cards.Count == 0)
        {
            offset += AIEncodingSchema.CardFeatureCount;
            return;
        }

        float[] encoded = new float[AIEncodingSchema.CardFeatureCount];
        int count = 0;
        foreach (CardStateSnapshot card in cards)
        {
            if (card == null || card.Data == null)
            {
                continue;
            }
            Array.Clear(encoded, 0, encoded.Length);
            int encodedOffset = 0;
            WriteCard(card.Data, card, encoded, ref encodedOffset);
            for (int feature = 0; feature < encoded.Length; feature++)
            {
                destination[start + feature] += encoded[feature];
            }
            count++;
        }

        if (count > 0)
        {
            for (int feature = 0; feature < AIEncodingSchema.CardFeatureCount; feature++)
            {
                destination[start + feature] /= count;
            }
        }
        offset += AIEncodingSchema.CardFeatureCount;
    }

    private static void WriteCardDataSummary(
        List<CardData> cards,
        float[] destination,
        ref int offset)
    {
        int start = offset;
        if (cards == null || cards.Count == 0)
        {
            offset += AIEncodingSchema.CardFeatureCount;
            return;
        }

        float[] encoded = new float[AIEncodingSchema.CardFeatureCount];
        int count = 0;
        foreach (CardData card in cards)
        {
            if (card == null)
            {
                continue;
            }
            Array.Clear(encoded, 0, encoded.Length);
            int encodedOffset = 0;
            WriteCard(card, null, encoded, ref encodedOffset);
            for (int feature = 0; feature < encoded.Length; feature++)
            {
                destination[start + feature] += encoded[feature];
            }
            count++;
        }

        if (count > 0)
        {
            for (int feature = 0; feature < AIEncodingSchema.CardFeatureCount; feature++)
            {
                destination[start + feature] /= count;
            }
        }
        offset += AIEncodingSchema.CardFeatureCount;
    }

    private static void WriteHiddenPoolSummary(
        PlayerStateSnapshot opponent,
        float[] destination,
        ref int offset)
    {
        List<CardData> pool = opponent != null && opponent.HiddenCardPool != null
            ? opponent.HiddenCardPool
            : new List<CardData>();
        WriteCardDataSummary(pool, destination, ref offset);
    }

    private static void WriteCard(
        CardData data,
        CardStateSnapshot state,
        float[] destination,
        ref int offset)
    {
        int start = offset;
        destination[offset++] = data != null ? 1f : 0f;
        WriteOneHot(destination, ref offset, data != null ? IndexOf(CardTypes, data.cardType) : -1, AIEncodingSchema.CardTypeCount);

        destination[offset++] = Normalize(data != null ? data.cost : 0, GameConst.costMax);
        destination[offset++] = NormalizeStat(data != null ? data.attack : 0);
        destination[offset++] = NormalizeStat(data != null ? data.health : 0);
        destination[offset++] = NormalizeStat(state != null ? state.Attack : 0);
        destination[offset++] = NormalizeStat(state != null ? state.Health : 0);
        destination[offset++] = NormalizeStat(state != null ? state.MaxHealth : 0);

        WriteOneHot(destination, ref offset, state != null ? IndexOf(CardStates, state.State) : -1, AIEncodingSchema.CardStateCount);
        destination[offset++] = state != null && state.CanAttack ? 1f : 0f;
        destination[offset++] = state != null && state.CanAttackPlayer ? 1f : 0f;
        destination[offset++] = Normalize(state != null ? state.AttacksRemaining : 0, 2);
        destination[offset++] = Normalize(state != null ? state.HolyShield : 0, 2);
        destination[offset++] = state != null && state.IsStealth ? 1f : 0f;
        destination[offset++] = state != null && state.CastUsed ? 1f : 0f;
        destination[offset++] = state != null && state.IsSilence ? 1f : 0f;
        destination[offset++] = state != null && state.IsDying ? 1f : 0f;

        for (int index = 0; index < PassiveTypes.Length; index++)
        {
            destination[offset++] = data != null && data.passiveTypes != null && data.passiveTypes.Contains(PassiveTypes[index]) ? 1f : 0f;
        }

        int triggerOffset = offset;
        offset += AIEncodingSchema.TriggerTypeCount;
        int conditionOffset = offset;
        offset += AIEncodingSchema.ConditionTypeCount;
        int effectOffset = offset;
        offset += AIEncodingSchema.EffectTypeCount;
        int valuesOffset = offset;
        offset += 3;
        int countOffset = offset++;
        int effectCount = 0;
        AccumulateEffects(data != null ? data.effects : null, destination, triggerOffset, conditionOffset, effectOffset, valuesOffset, ref effectCount, 0);
        destination[countOffset] = Normalize(effectCount, 8);

        EnsureOffset(offset - start, AIEncodingSchema.CardFeatureCount, "card");
    }

    private static void AccumulateEffects(
        List<CardEffectData> effects,
        float[] destination,
        int triggerOffset,
        int conditionOffset,
        int effectOffset,
        int valuesOffset,
        ref int effectCount,
        int depth)
    {
        if (effects == null || depth > 8)
        {
            return;
        }

        foreach (CardEffectData effect in effects)
        {
            if (effect == null)
            {
                continue;
            }
            effectCount++;
            SetPresent(destination, triggerOffset, IndexOf(TriggerTypes, effect.triggerType), AIEncodingSchema.TriggerTypeCount);
            SetPresent(destination, effectOffset, IndexOf(EffectTypes, effect.effectType), AIEncodingSchema.EffectTypeCount);

            if (effect.conditionTypes != null)
            {
                foreach (ConditionType condition in effect.conditionTypes)
                {
                    SetPresent(destination, conditionOffset, IndexOf(ConditionTypes, condition), AIEncodingSchema.ConditionTypeCount);
                }
            }
            if (effect.effectValues != null)
            {
                for (int index = 0; index < Math.Min(3, effect.effectValues.Length); index++)
                {
                    destination[valuesOffset + index] += NormalizeStat(effect.effectValues[index]);
                }
            }

            AccumulateEffects(effect.thenEffects, destination, triggerOffset, conditionOffset, effectOffset, valuesOffset, ref effectCount, depth + 1);
            AccumulateEffects(effect.elseEffects, destination, triggerOffset, conditionOffset, effectOffset, valuesOffset, ref effectCount, depth + 1);
        }
    }

    private static void WriteTarget(
        BattleStateSnapshot state,
        int observerPlayerIndex,
        SimulatedTarget target,
        float[] destination,
        ref int offset)
    {
        WriteOneHot(destination, ref offset, target != null ? (int)target.Kind : -1, 2);
        if (target == null)
        {
            offset += 3 + AIEncodingSchema.ZoneTypeCount + 1 + AIEncodingSchema.CardFeatureCount + 4;
            return;
        }

        if (target.Kind == SimulatedTargetKind.Player)
        {
            PlayerStateSnapshot player = state.GetPlayer(target.Id);
            WriteRelation(destination, ref offset, player != null ? player.PlayerIndex : -1, observerPlayerIndex);
            WriteOneHot(destination, ref offset, 5, AIEncodingSchema.ZoneTypeCount);
            destination[offset++] = 0f;
            WriteCard(null, null, destination, ref offset);
            destination[offset++] = Normalize(player != null ? player.Health : 0, GameConst.initalHealth);
            destination[offset++] = Normalize(player != null ? player.MaxHealth : 0, GameConst.initalHealth);
            destination[offset++] = Normalize(player != null ? player.Cost : 0, GameConst.costMax);
            destination[offset++] = Normalize(player != null ? player.MaxCost : 0, GameConst.costMax);
            return;
        }

        CardStateSnapshot card = state.FindCard(target.Id);
        WriteRelation(destination, ref offset, card != null ? card.OwnerIndex : -1, observerPlayerIndex);
        CardLocation location = FindCardLocation(state, card);
        WriteOneHot(destination, ref offset, location.ZoneIndex, AIEncodingSchema.ZoneTypeCount);
        destination[offset++] = location.NormalizedIndex;
        WriteCard(card != null ? card.Data : null, card, destination, ref offset);
        offset += 4;
    }

    private static void WriteRelation(float[] destination, ref int offset, int ownerIndex, int observerPlayerIndex)
    {
        int relation = ownerIndex < 0 ? 2 : ownerIndex == observerPlayerIndex ? 0 : 1;
        WriteOneHot(destination, ref offset, relation, 3);
    }

    private static CardLocation FindCardLocation(BattleStateSnapshot state, CardStateSnapshot card)
    {
        if (state == null || card == null)
        {
            return CardLocation.Missing;
        }

        PlayerStateSnapshot owner = state.GetPlayer(card.OwnerIndex);
        if (owner == null)
        {
            return CardLocation.Missing;
        }

        return card.State switch
        {
            CardState.Hand => CardLocation.From(owner.Hand.IndexOf(card), 1, GameConst.handMax),
            CardState.Field => CardLocation.From(owner.Field.IndexOf(card), 2, GameConst.fieldMax),
            CardState.Graveyard => CardLocation.From(owner.Graveyard.IndexOf(card), 3, GameConst.librarymax),
            CardState.Hanging => CardLocation.From(owner.Hand.IndexOf(card), 4, GameConst.handMax),
            _ => CardLocation.From(owner.DeckRemaining.IndexOf(card), 0, GameConst.librarymax),
        };
    }

    private static void WriteOneHot(float[] destination, ref int offset, int index, int count)
    {
        if (index >= 0 && index < count)
        {
            destination[offset + index] = 1f;
        }
        offset += count;
    }

    private static void SetPresent(float[] destination, int offset, int index, int count)
    {
        if (index >= 0 && index < count)
        {
            destination[offset + index] = 1f;
        }
    }

    private static int IndexOf<T>(T[] values, T value) where T : struct
    {
        for (int index = 0; index < values.Length; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
            {
                return index;
            }
        }
        return -1;
    }

    private static float Normalize(int value, int maximum)
    {
        if (maximum <= 0)
        {
            return 0f;
        }
        return Clamp(value / (float)maximum, -2f, 2f);
    }

    private static float NormalizeStat(int value)
    {
        return Clamp(value / 30f, -2f, 2f);
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private static void EnsureOffset(int actual, int expected, string featureName)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"Encoded {featureName} feature length {actual} does not match schema {expected}.");
        }
    }

    private readonly struct CardLocation
    {
        public static readonly CardLocation Missing = new(-1, 0f);

        private CardLocation(int zoneIndex, float normalizedIndex)
        {
            ZoneIndex = zoneIndex;
            NormalizedIndex = normalizedIndex;
        }

        public int ZoneIndex { get; }
        public float NormalizedIndex { get; }

        public static CardLocation From(int index, int zoneIndex, int maximum)
        {
            return new CardLocation(zoneIndex, index >= 0 ? Normalize(index, Math.Max(1, maximum - 1)) : 0f);
        }
    }
}
