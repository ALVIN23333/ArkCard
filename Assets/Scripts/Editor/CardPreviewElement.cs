using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class CardPreviewElement : VisualElement
{
    private readonly Label typeBadge;
    private readonly Label passiveBadge;
    private readonly Label costValue;
    private readonly Label attackValue;
    private readonly Label healthValue;
    private readonly Label nameLabel;
    private readonly Label descriptionLabel;
    private readonly Image artwork;
    private readonly VisualElement statsRow;
    private readonly Label placeholderLabel;

    public CardPreviewElement()
    {
        style.paddingBottom = 12;
        style.paddingLeft = 12;
        style.paddingRight = 12;
        style.paddingTop = 12;

        VisualElement frame = new();
        frame.style.backgroundColor = new Color(0.15f, 0.17f, 0.2f);
        frame.style.borderBottomWidth = 1;
        frame.style.borderLeftWidth = 1;
        frame.style.borderRightWidth = 1;
        frame.style.borderTopWidth = 1;
        frame.style.borderBottomColor = new Color(0.33f, 0.38f, 0.45f);
        frame.style.borderLeftColor = new Color(0.33f, 0.38f, 0.45f);
        frame.style.borderRightColor = new Color(0.33f, 0.38f, 0.45f);
        frame.style.borderTopColor = new Color(0.33f, 0.38f, 0.45f);
        frame.style.borderBottomLeftRadius = 12;
        frame.style.borderBottomRightRadius = 12;
        frame.style.borderTopLeftRadius = 12;
        frame.style.borderTopRightRadius = 12;
        frame.style.paddingBottom = 14;
        frame.style.paddingLeft = 14;
        frame.style.paddingRight = 14;
        frame.style.paddingTop = 14;
        Add(frame);

        VisualElement badgeRow = new();
        badgeRow.style.flexDirection = FlexDirection.Row;
        badgeRow.style.justifyContent = Justify.SpaceBetween;
        badgeRow.style.marginBottom = 8;
        frame.Add(badgeRow);

        typeBadge = CreateBadge(new Color(0.25f, 0.38f, 0.55f));
        passiveBadge = CreateBadge(new Color(0.36f, 0.28f, 0.17f));
        badgeRow.Add(typeBadge);
        badgeRow.Add(passiveBadge);

        artwork = new Image();
        artwork.scaleMode = ScaleMode.ScaleToFit;
        artwork.style.height = 220;
        artwork.style.marginBottom = 10;
        artwork.style.unityBackgroundImageTintColor = Color.white;
        frame.Add(artwork);

        placeholderLabel = new Label("未设置图片");
        placeholderLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        placeholderLabel.style.color = new Color(0.7f, 0.74f, 0.78f);
        placeholderLabel.style.marginBottom = 10;
        frame.Add(placeholderLabel);

        nameLabel = new Label();
        nameLabel.style.fontSize = 18;
        nameLabel.style.color = Color.white;
        nameLabel.style.marginBottom = 8;
        frame.Add(nameLabel);

        VisualElement numberRow = new();
        numberRow.style.flexDirection = FlexDirection.Row;
        numberRow.style.justifyContent = Justify.SpaceBetween;
        numberRow.style.marginBottom = 10;
        frame.Add(numberRow);

        costValue = CreateStatBlock(numberRow, "费用");
        statsRow = new VisualElement();
        statsRow.style.flexDirection = FlexDirection.Row;
        numberRow.Add(statsRow);
        attackValue = CreateStatBlock(statsRow, "攻击");
        healthValue = CreateStatBlock(statsRow, "生命");

        descriptionLabel = new Label();
        descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
        descriptionLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
        descriptionLabel.style.unityTextAlign = TextAnchor.UpperLeft;
        descriptionLabel.style.minHeight = 88;
        frame.Add(descriptionLabel);
    }

    public void SetCard(CardData card)
    {
        if (card == null)
        {
            typeBadge.text = string.Empty;
            passiveBadge.text = string.Empty;
            costValue.text = "-";
            attackValue.text = "-";
            healthValue.text = "-";
            nameLabel.text = "未选择卡牌";
            descriptionLabel.text = "请先从左侧选择一张卡牌。";
            artwork.image = null;
            placeholderLabel.style.display = DisplayStyle.Flex;
            statsRow.style.display = DisplayStyle.None;
            return;
        }

        typeBadge.text = EditorLabelUtility.GetCardTypeLabel(card.cardType);
        passiveBadge.text = FormatPassives(card);
        costValue.text = card.cost.ToString();
        attackValue.text = card.attack.ToString();
        healthValue.text = card.health.ToString();
        nameLabel.text = string.IsNullOrWhiteSpace(card.name) ? "(未命名卡牌)" : card.name;
        descriptionLabel.text = string.IsNullOrWhiteSpace(card.effectDescription)
            ? "当前没有描述。"
            : card.effectDescription.Replace("\\n", "\n");

        if (card.image != null && card.image.texture != null)
        {
            artwork.image = card.image.texture;
            placeholderLabel.style.display = DisplayStyle.None;
        }
        else
        {
            artwork.image = null;
            placeholderLabel.style.display = DisplayStyle.Flex;
        }

        statsRow.style.display = card.cardType == CardType.Minion ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static string FormatPassives(CardData card)
    {
        if (card.passiveTypes == null || card.passiveTypes.Count == 0)
        {
            return "无被动";
        }

        List<string> labels = new();
        foreach (PassiveType passive in card.passiveTypes)
        {
            if (passive == PassiveType.None)
            {
                continue;
            }

            labels.Add(EditorLabelUtility.GetPassiveTypeLabel(passive));
        }

        return labels.Count == 0 ? "无被动" : string.Join(" / ", labels);
    }

    private static Label CreateBadge(Color backgroundColor)
    {
        Label label = new();
        label.style.backgroundColor = backgroundColor;
        label.style.color = Color.white;
        label.style.paddingBottom = 4;
        label.style.paddingLeft = 8;
        label.style.paddingRight = 8;
        label.style.paddingTop = 4;
        label.style.borderBottomLeftRadius = 999;
        label.style.borderBottomRightRadius = 999;
        label.style.borderTopLeftRadius = 999;
        label.style.borderTopRightRadius = 999;
        return label;
    }

    private static Label CreateStatBlock(VisualElement parent, string title)
    {
        VisualElement wrapper = new();
        wrapper.style.alignItems = Align.Center;
        wrapper.style.paddingBottom = 4;
        wrapper.style.paddingLeft = 8;
        wrapper.style.paddingRight = 8;
        wrapper.style.paddingTop = 4;
        wrapper.style.backgroundColor = new Color(0.2f, 0.22f, 0.27f);
        wrapper.style.borderBottomLeftRadius = 8;
        wrapper.style.borderBottomRightRadius = 8;
        wrapper.style.borderTopLeftRadius = 8;
        wrapper.style.borderTopRightRadius = 8;
        if (parent.childCount > 0)
        {
            wrapper.style.marginLeft = 8;
        }

        parent.Add(wrapper);

        Label titleLabel = new(title);
        titleLabel.style.fontSize = 11;
        titleLabel.style.color = new Color(0.78f, 0.82f, 0.86f);
        wrapper.Add(titleLabel);

        Label valueLabel = new();
        valueLabel.style.fontSize = 16;
        valueLabel.style.color = Color.white;
        wrapper.Add(valueLabel);
        return valueLabel;
    }
}
