using System;

public static class AIEncodingSchema
{
    // Schema v2: unified configurable effects (Damage=110 ... Silence=121) added
    // 12 new EffectType values, so card features and all derived dimensions grew.
    public const int Version = 2;

    public const int CardTypeCount = 3;
    public const int CardStateCount = 5;
    public const int PassiveTypeCount = 11;
    public const int TriggerTypeCount = 7;
    public const int ConditionTypeCount = 11;
    public const int EffectTypeCount = 34;

    public const int GlobalFeatureCount = 20;
    public const int CardFeatureCount = 90;
    public const int MaxHandCards = GameConst.handMax;
    public const int MaxFieldCards = GameConst.fieldMax;
    public const int VisibleCardSlotCount = MaxHandCards + MaxFieldCards + MaxFieldCards;
    public const int CardSummarySlotCount = 4;
    public const int StateFeatureCount = GlobalFeatureCount
        + (VisibleCardSlotCount + CardSummarySlotCount) * CardFeatureCount;

    public const int ActionTypeCount = 5;
    public const int ZoneTypeCount = 6;
    public const int MaxActionTargets = 6;
    public const int TargetFeatureCount = 2 + 3 + ZoneTypeCount + 1 + CardFeatureCount + 4;
    public const int ActionFeatureCount = ActionTypeCount
        + ZoneTypeCount + 1 + CardFeatureCount
        + 1 + MaxActionTargets * TargetFeatureCount;
    public const int PolicyInputFeatureCount = StateFeatureCount + ActionFeatureCount;

    public static bool IsRuntimeCompatible(out string error)
    {
        if (Enum.GetValues(typeof(CardType)).Length != CardTypeCount
            || Enum.GetValues(typeof(CardState)).Length != CardStateCount
            || Enum.GetValues(typeof(PassiveType)).Length != PassiveTypeCount
            || Enum.GetValues(typeof(TriggerType)).Length != TriggerTypeCount
            || Enum.GetValues(typeof(ConditionType)).Length != ConditionTypeCount
            || Enum.GetValues(typeof(EffectType)).Length != EffectTypeCount
            || Enum.GetValues(typeof(SimulatedActionType)).Length != ActionTypeCount)
        {
            error = "A game enum changed without incrementing the AI feature schema version.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
