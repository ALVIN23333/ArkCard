using System.Collections.Generic;

internal enum CardValidationSeverity
{
    Info,
    Warning,
    Error,
}

internal sealed class CardValidationMessage
{
    public CardValidationMessage(CardValidationSeverity severity, int cardIndex, string propertyPath, string message)
    {
        Severity = severity;
        CardIndex = cardIndex;
        PropertyPath = propertyPath;
        Message = message;
    }

    public CardValidationSeverity Severity { get; }
    public int CardIndex { get; }
    public string PropertyPath { get; }
    public string Message { get; }
}

internal static class CardValidationService
{
    public static List<CardValidationMessage> Validate(CardListSO database, int selectedCardIndex)
    {
        List<CardValidationMessage> messages = new();
        if (database == null || database.cards == null || selectedCardIndex < 0 || selectedCardIndex >= database.cards.Count)
        {
            return messages;
        }

        CardData card = database.cards[selectedCardIndex];
        string cardPath = GetCardPropertyPath(selectedCardIndex);
        Dictionary<int, int> idCounts = BuildIdCounts(database);

        if (idCounts.TryGetValue(card.index, out int count) && count > 1)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Error, selectedCardIndex, $"{cardPath}.index", "卡牌 ID 重复。"));
        }

        if (string.IsNullOrWhiteSpace(card.name))
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Error, selectedCardIndex, $"{cardPath}.name", "卡牌名称不能为空。"));
        }

        if (card.cardType == CardType.Minion)
        {
            if (card.attack <= 0)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.attack", "随从攻击通常应大于 0。"));
            }

            if (card.health <= 0)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.health", "随从生命通常应大于 0。"));
            }

            if (card.index < 1000 || card.index >= 1100)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.index", "随从卡建议使用 1000 段 ID。"));
            }
        }
        else if (card.cardType == CardType.SPELL)
        {
            if (card.attack != 0)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.attack", "法术卡攻击通常应为 0。"));
            }

            if (card.health != 0)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.health", "法术卡生命通常应为 0。"));
            }

            if (HasPassives(card))
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.passiveTypes", "法术卡通常不应配置被动。"));
            }

            if (card.index < 1100 || card.index >= 1200)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.index", "法术卡建议使用 1100 段 ID。"));
            }
        }

        if (card.image == null)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.image", "卡牌图片未设置。"));
        }

        if (string.IsNullOrWhiteSpace(card.effectDescription))
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Info, selectedCardIndex, $"{cardPath}.effectDescription", "卡牌描述为空。"));
        }

        if (card.aiBasePriority < -20 || card.aiBasePriority > 20)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.aiBasePriority", "AI 基础优先级建议保持在 -20 到 20 之间。"));
        }

        if (card.aiPlayStyle == AIPlayStyle.ComboReserve && card.aiComboReserveThreshold <= 0)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.aiComboReserveThreshold", "AI 打法为保留连携，但连携保留阈值必须大于 0。"));
        }

        if (card.aiComboReserveThreshold > GameConst.costMax)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, selectedCardIndex, $"{cardPath}.aiComboReserveThreshold", $"连携保留阈值高于费用上限 {GameConst.costMax}，请确认配置意图。"));
        }

        if (card.aiRole == CardAIRole.Finisher && card.aiLethalBonus == 0)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Info, selectedCardIndex, $"{cardPath}.aiLethalBonus", "终结牌尚未配置斩杀加成。"));
        }

        if (card.aiRole != CardAIRole.Finisher && card.aiLethalBonus > 10)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Info, selectedCardIndex, $"{cardPath}.aiLethalBonus", "非终结牌的斩杀加成高于 10，请确认配置意图。"));
        }

        if (card.effects != null)
        {
            for (int i = 0; i < card.effects.Count; i++)
            {
                ValidateEffect(database, card, card.effects[i], selectedCardIndex, $"{cardPath}.effects.Array.data[{i}]", true, messages);
            }
        }

        if (card.passiveTypes != null)
        {
            for (int i = 0; i < card.passiveTypes.Count; i++)
            {
                PassiveType passive = card.passiveTypes[i];
                if (passive == PassiveType.None)
                {
                    continue;
                }

                for (int j = i + 1; j < card.passiveTypes.Count; j++)
                {
                    if (card.passiveTypes[j] == passive)
                    {
                        messages.Add(new CardValidationMessage(
                            CardValidationSeverity.Warning,
                            selectedCardIndex,
                            $"{cardPath}.passiveTypes.Array.data[{i}]",
                            "被动配置重复，请保留其中一项。"));
                        break;
                    }
                }
            }
        }

        messages.Sort((left, right) => right.Severity.CompareTo(left.Severity));
        return messages;
    }

    public static string GetCardPropertyPath(int cardIndex)
    {
        return $"cards.Array.data[{cardIndex}]";
    }

    private static void ValidateEffect(
        CardListSO database,
        CardData card,
        CardEffectData effect,
        int cardIndex,
        string effectPath,
        bool isTopLevel,
        List<CardValidationMessage> messages)
    {
        if (effect == null)
        {
            return;
        }

        if (effect.effectType != EffectType.None && !EffectRegistry.IsRegistered(effect.effectType))
        {
            messages.Add(new CardValidationMessage(
                CardValidationSeverity.Warning,
                cardIndex,
                $"{effectPath}.effectType",
                "效果类型未注册，运行时/编辑器/AI 无法处理。"));
        }

        if (effect.effectType != EffectType.None && EffectRegistry.TryGetMissingRequiredParameter(effect, out EffectValueParameter missingParameter))
        {
            messages.Add(new CardValidationMessage(
                CardValidationSeverity.Error,
                cardIndex,
                $"{effectPath}.effectValues",
                $"效果参数不完整，缺少：{missingParameter.Label}。"));
        }

        ValidateUnifiedEffect(database, card, effect, cardIndex, effectPath, messages);

        if (card.cardType == CardType.SPELL && isTopLevel && effect.triggerType != TriggerType.None)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Warning, cardIndex, $"{effectPath}.triggerType", "法术卡顶层效果建议使用 TriggerType.None。"));
        }

        if (HasConditions(effect.conditionTypes) && !HasEffects(effect.thenEffects) && !HasEffects(effect.elseEffects))
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Info, cardIndex, effectPath, "当前条件效果没有 then/else 分支。"));
        }

        if (HasEffects(effect.thenEffects))
        {
            for (int i = 0; i < effect.thenEffects.Count; i++)
            {
                ValidateEffect(database, card, effect.thenEffects[i], cardIndex, $"{effectPath}.thenEffects.Array.data[{i}]", false, messages);
            }
        }

        if (HasEffects(effect.elseEffects))
        {
            for (int i = 0; i < effect.elseEffects.Count; i++)
            {
                ValidateEffect(database, card, effect.elseEffects[i], cardIndex, $"{effectPath}.elseEffects.Array.data[{i}]", false, messages);
            }
        }
    }

    private static void ValidateUnifiedEffect(
        CardListSO database,
        CardData card,
        CardEffectData effect,
        int cardIndex,
        string effectPath,
        List<CardValidationMessage> messages)
    {
        if (!EffectEditorCatalog.IsUnified(effect.effectType))
        {
            return;
        }

        int countIndex = effect.effectType is EffectType.Destroy or EffectType.BackHand or EffectType.Discard or EffectType.Revive ? 0
            : effect.effectType == EffectType.Buff ? 2
            : effect.effectType == EffectType.Silence ? 0 : 1;
        bool countRequired = effect.effectType is EffectType.Discard or EffectType.Revive
            or EffectType.SummonMinion or EffectType.SummonRandomCostMinion
            || (effect.effectType == EffectType.Silence && (effect.targetMode is EffectTargetMode.Selected or EffectTargetMode.Random))
            || (effect.targetMode is EffectTargetMode.Selected or EffectTargetMode.Random);
        if (countRequired && EffectValues.GetValue(effect, countIndex) <= 0)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Error, cardIndex, $"{effectPath}.effectValues", "效果数量必须大于 0。"));
        }

        if ((effect.effectType is EffectType.Damage or EffectType.Heal) && EffectValues.GetValue(effect, 0) <= 0)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Error, cardIndex, $"{effectPath}.effectValues", "伤害或治疗数值必须大于 0。"));
        }

        if (effect.effectType == EffectType.DrawCards && EffectValues.GetValue(effect, 0) <= 0)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Error, cardIndex, $"{effectPath}.effectValues", "抽牌数量必须大于 0。"));
        }

        if (effect.effectType == EffectType.Cost)
        {
            int currentCost = EffectValues.GetValue(effect, 0);
            int maxCost = EffectValues.GetValue(effect, 1);
            if (currentCost < 0 || maxCost < 0)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Error, cardIndex, $"{effectPath}.effectValues", "费用增量不能为负数。"));
            }
            else if (currentCost == 0 && maxCost == 0)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Error, cardIndex, $"{effectPath}.effectValues", "当前费用或费用上限至少增加一项。"));
            }
        }

        if (effect.effectType == EffectType.Silence && effect.targetMode == EffectTargetMode.Self
            && card.cardType == CardType.SPELL)
        {
            messages.Add(new CardValidationMessage(CardValidationSeverity.Error, cardIndex, effectPath, "法术不能配置为沉默自身。"));
        }

        if (effect.effectType == EffectType.SummonMinion)
        {
            CardData summon = database != null ? database.GetData(EffectValues.GetValue(effect, 0)) : null;
            if (summon == null || summon.cardType != CardType.Minion)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Error, cardIndex, $"{effectPath}.effectValues", "召唤卡牌 ID 必须对应随从卡。"));
            }
        }

        if (effect.effectType == EffectType.SummonRandomCostMinion)
        {
            int cost = EffectValues.GetValue(effect, 0);
            bool found = false;
            if (database != null && database.cards != null)
            {
                foreach (CardData candidate in database.cards)
                {
                    if (candidate != null && candidate.cardType == CardType.Minion && candidate.cost == cost)
                    {
                        found = true;
                        break;
                    }
                }
            }
            if (!found)
            {
                messages.Add(new CardValidationMessage(CardValidationSeverity.Error, cardIndex, $"{effectPath}.effectValues", $"卡牌库中没有 {cost} 费随从。"));
            }
        }
    }

    private static Dictionary<int, int> BuildIdCounts(CardListSO database)
    {
        Dictionary<int, int> counts = new();
        foreach (CardData card in database.cards)
        {
            if (card == null)
            {
                continue;
            }

            if (!counts.ContainsKey(card.index))
            {
                counts.Add(card.index, 0);
            }

            counts[card.index]++;
        }

        return counts;
    }

    private static bool HasConditions(List<ConditionType> conditionTypes)
    {
        if (conditionTypes == null || conditionTypes.Count == 0)
        {
            return false;
        }

        foreach (ConditionType conditionType in conditionTypes)
        {
            if (conditionType != ConditionType.None)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEffects(List<CardEffectData> effects)
    {
        return effects != null && effects.Count > 0;
    }

    private static bool HasPassives(CardData card)
    {
        if (card.passiveTypes == null)
        {
            return false;
        }

        foreach (PassiveType passive in card.passiveTypes)
        {
            if (passive != PassiveType.None)
            {
                return true;
            }
        }

        return false;
    }
}
