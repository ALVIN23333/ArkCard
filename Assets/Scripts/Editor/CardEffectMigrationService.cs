using UnityEditor;
using UnityEngine;

public static class CardEffectMigrationService
{
    public const int CurrentVersion = 3;

    public static bool MigrateIfNeeded(CardListSO database)
    {
        if (database == null || database.effectSchemaVersion >= CurrentVersion)
        {
            return false;
        }

        Undo.RecordObject(database, "Migrate Card Effects");
        SerializedObject serialized = new(database);
        SerializedProperty cards = serialized.FindProperty("cards");
        for (int i = 0; i < cards.arraySize; i++)
        {
            MigrateEffectArray(cards.GetArrayElementAtIndex(i).FindPropertyRelative("effects"));
        }

        serialized.FindProperty("effectSchemaVersion").intValue = CurrentVersion;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssetIfDirty(database);
        return true;
    }

    [MenuItem("Tools/ArkCards/Migrate Unified Effects")]
    public static void MigrateDefaultDatabase()
    {
        CardListSO database = AssetDatabase.LoadAssetAtPath<CardListSO>("Assets/Resources/ArkCardsDatabase.asset");
        MigrateIfNeeded(database);
    }

    private static void MigrateEffectArray(SerializedProperty effects)
    {
        if (effects == null)
        {
            return;
        }

        for (int i = 0; i < effects.arraySize; i++)
        {
            SerializedProperty effect = effects.GetArrayElementAtIndex(i);
            MigrateEffect(effect);
            MigrateEffectArray(effect.FindPropertyRelative("thenEffects"));
            MigrateEffectArray(effect.FindPropertyRelative("elseEffects"));
        }
    }

    private static void MigrateEffect(SerializedProperty effect)
    {
        SerializedProperty typeProperty = effect.FindPropertyRelative("effectType");
        EffectType legacyType = EffectRegistry.GetEffectTypeAt(typeProperty.enumValueIndex);
        EffectType type = legacyType;
        EffectTargetMode mode = EffectTargetMode.All;
        EffectTargetSide side = EffectTargetSide.Friendly;
        EffectCharacterScope scope = EffectCharacterScope.Minions;
        bool includeSource = true;
        bool remapCountFromIndexOne = false;

        if (legacyType >= EffectType.Damage && legacyType <= EffectType.Silence)
        {
            NormalizeUnifiedEffect(effect, legacyType);
            return;
        }

        switch (legacyType)
        {
            case EffectType.DamageAll:
                type = EffectType.Damage; mode = EffectTargetMode.All; side = EffectTargetSide.Both;
                scope = EffectCharacterScope.Characters; includeSource = false; break;
            case EffectType.DamageAllEnemy:
                type = EffectType.Damage; mode = EffectTargetMode.All; side = EffectTargetSide.Enemy;
                scope = EffectCharacterScope.Characters; break;
            case EffectType.DealDamageToEnemy:
                type = EffectType.Damage; mode = EffectTargetMode.Selected; side = EffectTargetSide.Enemy;
                scope = EffectCharacterScope.Characters; break;
            case EffectType.healAlliesAll:
                type = EffectType.Heal; mode = EffectTargetMode.All; side = EffectTargetSide.Friendly;
                scope = EffectCharacterScope.Characters; break;
            case EffectType.HealAlly:
                type = EffectType.Heal; mode = EffectTargetMode.Selected; side = EffectTargetSide.Friendly; break;
            case EffectType.DestoryEnemy:
                type = EffectType.Destroy; mode = EffectTargetMode.Selected; side = EffectTargetSide.Enemy;
                remapCountFromIndexOne = true; break;
            case EffectType.BuffSelf:
                type = EffectType.Buff; mode = EffectTargetMode.Self; break;
            case EffectType.BuffAlliesAll:
                type = EffectType.Buff; mode = EffectTargetMode.All; side = EffectTargetSide.Friendly; break;
            case EffectType.BuffAllEnemies:
                type = EffectType.Buff; mode = EffectTargetMode.All; side = EffectTargetSide.Enemy; break;
            case EffectType.BuffAlly:
                type = EffectType.Buff; mode = EffectTargetMode.Selected; side = EffectTargetSide.Friendly; break;
            case EffectType.BuffEnemy:
                type = EffectType.Buff; mode = EffectTargetMode.Selected; side = EffectTargetSide.Enemy; break;
            case EffectType.OtherBackHand:
                type = EffectType.BackHand; mode = EffectTargetMode.Selected; side = EffectTargetSide.Both;
                includeSource = false; remapCountFromIndexOne = true; break;
            case EffectType.AllyBackHand:
                type = EffectType.BackHand; mode = EffectTargetMode.Selected; side = EffectTargetSide.Friendly;
                remapCountFromIndexOne = true; break;
            case EffectType.EnemyBackHand:
                type = EffectType.BackHand; mode = EffectTargetMode.Selected; side = EffectTargetSide.Enemy;
                remapCountFromIndexOne = true; break;
            case EffectType.DisCard:
                type = EffectType.Discard; mode = EffectTargetMode.Random; side = EffectTargetSide.Friendly; break;
            case EffectType.ReviveAlly:
                type = EffectType.Revive; mode = EffectTargetMode.Selected; side = EffectTargetSide.Friendly;
                remapCountFromIndexOne = true; break;
            case EffectType.Draw:
                type = EffectType.DrawCards; mode = EffectTargetMode.All; side = EffectTargetSide.Friendly; break;
            case EffectType.AddCost:
                type = EffectType.Cost; mode = EffectTargetMode.All; side = EffectTargetSide.Friendly;
                ConvertCostValues(effect, false, true); break;
            case EffectType.AddCostMax:
                type = EffectType.Cost; mode = EffectTargetMode.All; side = EffectTargetSide.Friendly;
                ConvertCostValues(effect, true, false); break;
            case EffectType.AddBothCost:
                type = EffectType.Cost; mode = EffectTargetMode.All; side = EffectTargetSide.Friendly;
                ConvertCostValues(effect, true, true); break;
            case EffectType.SlienceEnemy:
                type = EffectType.Silence; mode = EffectTargetMode.Selected; side = EffectTargetSide.Enemy;
                remapCountFromIndexOne = true; break;
            default:
                return;
        }

        typeProperty.enumValueIndex = EffectRegistry.GetLabelIndex(type);
        effect.FindPropertyRelative("targetMode").enumValueIndex = (int)mode;
        effect.FindPropertyRelative("targetSide").enumValueIndex = (int)side;
        effect.FindPropertyRelative("characterScope").enumValueIndex = (int)scope;
        effect.FindPropertyRelative("includeSource").boolValue = includeSource;

        SerializedProperty values = effect.FindPropertyRelative("effectValues");
        if (remapCountFromIndexOne)
        {
            int count = values.arraySize > 1 && values.GetArrayElementAtIndex(1).intValue > 0
                ? values.GetArrayElementAtIndex(1).intValue
                : 1;
            values.arraySize = 1;
            values.GetArrayElementAtIndex(0).intValue = count;
        }
        else
        {
            int requiredLength = EffectRegistry.Get(type).SuggestedArrayLength;
            if (values.arraySize < requiredLength)
            {
                int oldSize = values.arraySize;
                values.arraySize = requiredLength;
                for (int i = oldSize; i < requiredLength; i++) values.GetArrayElementAtIndex(i).intValue = i == 1 ? 1 : 0;
            }
        }

        NormalizeUnifiedEffect(effect, type);
    }

    private static void ConvertCostValues(SerializedProperty effect, bool includeMax, bool includeCurrent)
    {
        SerializedProperty values = effect.FindPropertyRelative("effectValues");
        int amount = values.arraySize > 0 ? values.GetArrayElementAtIndex(0).intValue : 0;
        values.arraySize = 2;
        values.GetArrayElementAtIndex(0).intValue = includeCurrent ? amount : 0;
        values.GetArrayElementAtIndex(1).intValue = includeMax ? amount : 0;
    }

    private static void NormalizeUnifiedEffect(SerializedProperty effect, EffectType type)
    {
        SerializedProperty values = effect.FindPropertyRelative("effectValues");
        EffectTargetMode mode = (EffectTargetMode)effect.FindPropertyRelative("targetMode").enumValueIndex;
        int countIndex = type switch
        {
            EffectType.Destroy or EffectType.BackHand or EffectType.Discard or EffectType.Revive => 0,
            EffectType.Buff => 2,
            EffectType.Damage or EffectType.Heal or EffectType.SummonMinion or EffectType.SummonRandomCostMinion => 1,
            EffectType.DrawCards => 0,
            EffectType.Silence => 0,
            EffectType.Cost => -1,
            _ => -1,
        };
        bool countRequired = type is EffectType.Discard or EffectType.Revive or EffectType.Silence
            or EffectType.SummonMinion or EffectType.SummonRandomCostMinion
            || mode is EffectTargetMode.Selected or EffectTargetMode.Random;
        if (type == EffectType.Cost)
        {
            if (values.arraySize < 2) values.arraySize = 2;
            if (values.GetArrayElementAtIndex(0).intValue < 0) values.GetArrayElementAtIndex(0).intValue = 0;
            if (values.GetArrayElementAtIndex(1).intValue < 0) values.GetArrayElementAtIndex(1).intValue = 0;
            return;
        }
        if (!countRequired || countIndex < 0)
        {
            return;
        }

        if (values.arraySize <= countIndex)
        {
            values.arraySize = countIndex + 1;
        }
        if (values.GetArrayElementAtIndex(countIndex).intValue <= 0)
        {
            values.GetArrayElementAtIndex(countIndex).intValue = 1;
        }
    }
}
