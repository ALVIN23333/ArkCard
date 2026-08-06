using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class CardPassiveEditorElement : VisualElement
{
    private readonly SerializedProperty passiveTypesProperty;
    private readonly Action<string, Action> applyChange;
    private readonly Action<string> setPendingNavigation;
    private readonly Action<string, VisualElement> registerNavigationTarget;
    private readonly VisualElement container;

    public CardPassiveEditorElement(
        SerializedProperty passiveTypesProperty,
        Action<string, Action> applyChange,
        Action<string> setPendingNavigation,
        Action<string, VisualElement> registerNavigationTarget)
    {
        this.passiveTypesProperty = passiveTypesProperty;
        this.applyChange = applyChange;
        this.setPendingNavigation = setPendingNavigation;
        this.registerNavigationTarget = registerNavigationTarget;

        style.flexDirection = FlexDirection.Column;

        Button addButton = new(() =>
        {
            applyChange("Add Passive", () =>
            {
                int newIndex = passiveTypesProperty.arraySize;
                passiveTypesProperty.arraySize++;
                SerializedProperty newElement = passiveTypesProperty.GetArrayElementAtIndex(newIndex);
                newElement.enumValueIndex = (int)PassiveType.None;
                setPendingNavigation?.Invoke(newElement.propertyPath);
            });
            Rebuild();
        })
        {
            text = "新增被动"
        };
        addButton.style.marginBottom = 8;
        Add(addButton);

        container = new VisualElement();
        container.style.flexDirection = FlexDirection.Column;
        Add(container);

        Rebuild();
    }

    private void Rebuild()
    {
        container.Clear();
        if (passiveTypesProperty == null)
        {
            return;
        }

        if (passiveTypesProperty.arraySize == 0)
        {
            container.Add(new HelpBox("当前卡牌没有配置被动。", HelpBoxMessageType.Info));
            return;
        }

        for (int i = 0; i < passiveTypesProperty.arraySize; i++)
        {
            BuildPassiveRow(i);
        }
    }

    private void BuildPassiveRow(int index)
    {
        int capturedIndex = index;
        SerializedProperty element = passiveTypesProperty.GetArrayElementAtIndex(index);
        if (element == null)
        {
            return;
        }

        Box rowBox = new();
        rowBox.style.flexDirection = FlexDirection.Row;
        rowBox.style.alignItems = Align.Center;
        rowBox.style.marginBottom = 6;
        rowBox.style.width = Length.Percent(100);
        container.Add(rowBox);

        PopupField<string> passiveField = new($"被动 {capturedIndex + 1}", EditorLabelUtility.GetPassiveTypeLabels(), element.enumValueIndex);
        passiveField.style.flexGrow = 1;
        passiveField.style.flexShrink = 1;
        passiveField.style.minWidth = 0;
        passiveField.style.marginRight = 6;
        passiveField.RegisterValueChangedCallback(_ =>
        {
            applyChange("Update Passive", () =>
            {
                element.enumValueIndex = passiveField.index;
            });
            setPendingNavigation?.Invoke(element.propertyPath);
        });
        registerNavigationTarget?.Invoke(element.propertyPath, passiveField);
        rowBox.Add(passiveField);

        Button deleteButton = new(() =>
        {
            applyChange("Delete Passive", () =>
            {
                passiveTypesProperty.DeleteArrayElementAtIndex(capturedIndex);
                setPendingNavigation?.Invoke(passiveTypesProperty.propertyPath);
            });
            Rebuild();
        })
        {
            text = "删除"
        };
        deleteButton.style.flexGrow = 0;
        deleteButton.style.flexShrink = 0;
        deleteButton.style.width = 56;
        rowBox.Add(deleteButton);
    }
}
