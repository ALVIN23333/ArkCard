using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class CardEffectEditorElement : VisualElement
{
    private enum NavigationMode
    {
        None,
        FocusOnly,
        ScrollToTarget,
    }

    private readonly Dictionary<string, bool> expansionState;
    private readonly Dictionary<string, VisualElement> localNavigationTargets = new();
    private readonly SerializedProperty effectsProperty;
    private readonly CardType cardType;
    private readonly CardListSO database;
    private readonly Action<string, Action> applyChange;
    private readonly Action<string> setPendingNavigation;
    private readonly Action<string, VisualElement> registerNavigationTarget;
    private readonly Func<bool> shouldExpandAll;
    private readonly VisualElement effectsContainer;

    private string pendingLocalNavigationPath;
    private NavigationMode pendingNavigationMode;

    public CardEffectEditorElement(
        SerializedProperty effectsProperty,
        CardType cardType,
        CardListSO database,
        Action<string, Action> applyChange,
        Action rebuildOnly,
        Action<string> setPendingNavigation,
        Action<string, VisualElement> registerNavigationTarget,
        Dictionary<string, bool> expansionState,
        Func<bool> shouldExpandAll)
    {
        this.effectsProperty = effectsProperty;
        this.cardType = cardType;
        this.database = database;
        this.applyChange = applyChange;
        this.setPendingNavigation = setPendingNavigation;
        this.registerNavigationTarget = registerNavigationTarget;
        this.expansionState = expansionState;
        this.shouldExpandAll = shouldExpandAll;

        style.flexDirection = FlexDirection.Column;

        Button addButton = new(() =>
        {
            applyChange("Add Card Effect", () =>
            {
                int newIndex = effectsProperty.arraySize;
                effectsProperty.arraySize++;
                SerializedProperty newEffect = effectsProperty.GetArrayElementAtIndex(newIndex);
                InitializeEffectProperty(newEffect, cardType == CardType.SPELL);
                SetNavigationTarget(newEffect.propertyPath, NavigationMode.ScrollToTarget);
                setPendingNavigation?.Invoke(newEffect.propertyPath);
            });
            RebuildRootEffects();
        })
        {
            text = "新增效果"
        };
        addButton.style.marginBottom = 8;
        Add(addButton);

        effectsContainer = new VisualElement();
        effectsContainer.style.flexDirection = FlexDirection.Column;
        Add(effectsContainer);

        BuildRootEffects();
    }

    private void BuildRootEffects()
    {
        localNavigationTargets.Clear();
        effectsContainer.Clear();

        if (effectsProperty.arraySize == 0)
        {
            effectsContainer.Add(new HelpBox("当前卡牌还没有效果。", HelpBoxMessageType.Info));
            return;
        }

        for (int i = 0; i < effectsProperty.arraySize; i++)
        {
            EffectCardView cardView = new(this, effectsProperty, i, 0, cardType == CardType.SPELL, RebuildRootEffects);
            effectsContainer.Add(cardView.Root);
        }
    }

    private void RebuildRootEffects()
    {
        PreserveScrollAndRun(BuildRootEffects);
    }

    private void RebuildRootEffects(string navigationPath, NavigationMode navigationMode)
    {
        SetNavigationTarget(navigationPath, navigationMode);
        PreserveScrollAndRun(BuildRootEffects);
    }

    private void PreserveScrollAndRun(Action rebuildAction)
    {
        ScrollView scrollView = GetFirstAncestorOfType<ScrollView>();
        Vector2 scrollOffset = scrollView != null ? scrollView.scrollOffset : Vector2.zero;

        rebuildAction?.Invoke();

        schedule.Execute(() =>
        {
            if (scrollView != null)
            {
                scrollView.scrollOffset = scrollOffset;
            }

            NavigateToPendingLocalTarget();
        }).ExecuteLater(0);
    }

    private void SetNavigationTarget(string path, NavigationMode navigationMode)
    {
        pendingLocalNavigationPath = path;
        pendingNavigationMode = navigationMode;
    }

    private void RegisterTarget(string path, VisualElement element)
    {
        if (string.IsNullOrEmpty(path) || element == null)
        {
            return;
        }

        localNavigationTargets[path] = element;
        registerNavigationTarget?.Invoke(path, element);
    }

    private void RemoveLocalTargetsUnder(string pathPrefix)
    {
        if (string.IsNullOrEmpty(pathPrefix))
        {
            return;
        }

        List<string> keysToRemove = new();
        foreach (string key in localNavigationTargets.Keys)
        {
            if (key == pathPrefix || key.StartsWith(pathPrefix, StringComparison.Ordinal))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (string key in keysToRemove)
        {
            localNavigationTargets.Remove(key);
        }
    }

    private void NavigateToPendingLocalTarget()
    {
        if (string.IsNullOrEmpty(pendingLocalNavigationPath))
        {
            pendingNavigationMode = NavigationMode.None;
            return;
        }

        string lookupPath = pendingLocalNavigationPath;
        while (!string.IsNullOrEmpty(lookupPath))
        {
            if (localNavigationTargets.TryGetValue(lookupPath, out VisualElement target) && target.panel != null)
            {
                if (pendingNavigationMode == NavigationMode.ScrollToTarget)
                {
                    ScrollView scrollView = target.GetFirstAncestorOfType<ScrollView>();
                    scrollView?.ScrollTo(target);
                }

                target.Focus();
                break;
            }

            int trimIndex = lookupPath.LastIndexOf('.');
            lookupPath = trimIndex > 0 ? lookupPath.Substring(0, trimIndex) : null;
        }

        pendingLocalNavigationPath = null;
        pendingNavigationMode = NavigationMode.None;
    }

    private static Button CreateActionButton(string text, Action action, bool addLeftMargin)
    {
        Button button = new(action) { text = text };
        if (addLeftMargin)
        {
            button.style.marginLeft = 4;
        }

        return button;
    }

    private static Box CreateInsetBox()
    {
        Box box = new();
        box.style.paddingBottom = 8;
        box.style.paddingLeft = 8;
        box.style.paddingRight = 8;
        box.style.paddingTop = 8;
        box.style.marginTop = 6;
        box.style.backgroundColor = new Color(0.14f, 0.15f, 0.18f);
        box.style.borderBottomLeftRadius = 6;
        box.style.borderBottomRightRadius = 6;
        box.style.borderTopLeftRadius = 6;
        box.style.borderTopRightRadius = 6;
        return box;
    }

    private static string GetEffectTitle(int effectIndex, SerializedProperty effectProperty)
    {
        TriggerType triggerType = (TriggerType)effectProperty.FindPropertyRelative("triggerType").enumValueIndex;
        // EffectType 是非连续枚举，enumValueIndex 是排序下标而非原始值，必须经注册表转换。
        EffectType effectType = EffectRegistry.GetEffectTypeAt(effectProperty.FindPropertyRelative("effectType").enumValueIndex);
        EffectTargetMode targetMode = (EffectTargetMode)effectProperty.FindPropertyRelative("targetMode").enumValueIndex;
        return $"效果 {effectIndex + 1}  {EditorLabelUtility.GetTriggerTypeLabel(triggerType)} / {EffectEditorCatalog.GetDisplayLabel(effectType, targetMode)}";
    }

    private static bool HasBranchEffects(SerializedProperty effectProperty)
    {
        return effectProperty.FindPropertyRelative("thenEffects").arraySize > 0
            || effectProperty.FindPropertyRelative("elseEffects").arraySize > 0;
    }

    private static bool HasConditions(SerializedProperty effectProperty)
    {
        SerializedProperty conditionProperty = effectProperty.FindPropertyRelative("conditionTypes");
        if (conditionProperty.arraySize == 0)
        {
            return false;
        }

        for (int i = 0; i < conditionProperty.arraySize; i++)
        {
            if ((ConditionType)conditionProperty.GetArrayElementAtIndex(i).enumValueIndex != ConditionType.None)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetEffectValue(SerializedProperty valuesProperty, int index, int defaultValue)
    {
        return index >= 0 && index < valuesProperty.arraySize
            ? valuesProperty.GetArrayElementAtIndex(index).intValue
            : defaultValue;
    }

    private static void EnsureArrayLength(SerializedProperty valuesProperty, int requiredLength)
    {
        if (valuesProperty.arraySize >= requiredLength)
        {
            return;
        }

        int oldSize = valuesProperty.arraySize;
        valuesProperty.arraySize = requiredLength;
        for (int i = oldSize; i < requiredLength; i++)
        {
            valuesProperty.GetArrayElementAtIndex(i).intValue = 0;
        }
    }

    private static void ResetEffectValuesForSchema(SerializedProperty valuesProperty, EffectType effectType)
    {
        ICardEffectDefinition definition = EffectRegistry.Get(effectType);
        int oldSize = valuesProperty.arraySize;
        int newSize = definition.SuggestedArrayLength;
        valuesProperty.arraySize = newSize;
        for (int i = oldSize; i < valuesProperty.arraySize; i++)
        {
            valuesProperty.GetArrayElementAtIndex(i).intValue = 0;
        }

        foreach (EffectValueParameter parameter in definition.Parameters)
        {
            SerializedProperty valueElement = valuesProperty.GetArrayElementAtIndex(parameter.Index);
            if (valueElement.intValue == 0 && parameter.DefaultValue != 0)
            {
                valueElement.intValue = parameter.DefaultValue;
            }
        }
    }

    private static void InitializeEffectProperty(SerializedProperty effectProperty, bool forceNoneTrigger)
    {
        effectProperty.FindPropertyRelative("triggerType").enumValueIndex = forceNoneTrigger ? (int)TriggerType.None : (int)TriggerType.Enter;
        effectProperty.FindPropertyRelative("conditionTypes").arraySize = 0;
        effectProperty.FindPropertyRelative("effectType").enumValueIndex = (int)EffectType.None;
        effectProperty.FindPropertyRelative("targetSide").enumValueIndex = (int)EffectTargetSide.Friendly;
        effectProperty.FindPropertyRelative("targetMode").enumValueIndex = (int)EffectTargetMode.All;
        effectProperty.FindPropertyRelative("characterScope").enumValueIndex = (int)EffectCharacterScope.Minions;
        effectProperty.FindPropertyRelative("includeSource").boolValue = true;
        effectProperty.FindPropertyRelative("effectValues").arraySize = 0;
        effectProperty.FindPropertyRelative("thenEffects").arraySize = 0;
        effectProperty.FindPropertyRelative("elseEffects").arraySize = 0;
    }

    private sealed class EffectCardView
    {
        private readonly CardEffectEditorElement owner;
        private readonly SerializedProperty parentArrayProperty;
        private readonly int effectIndex;
        private readonly int depth;
        private readonly bool isTopLevelSpell;
        private readonly Action<string, NavigationMode> rebuildScope;

        private string currentPath;
        private Box root;

        public EffectCardView(
            CardEffectEditorElement owner,
            SerializedProperty parentArrayProperty,
            int effectIndex,
            int depth,
            bool isTopLevelSpell,
            Action<string, NavigationMode> rebuildScope)
        {
            this.owner = owner;
            this.parentArrayProperty = parentArrayProperty;
            this.effectIndex = effectIndex;
            this.depth = depth;
            this.isTopLevelSpell = isTopLevelSpell;
            this.rebuildScope = rebuildScope;

            Build();
        }

        public VisualElement Root => root;

        private SerializedProperty GetEffectProperty()
        {
            return effectIndex >= 0 && effectIndex < parentArrayProperty.arraySize
                ? parentArrayProperty.GetArrayElementAtIndex(effectIndex)
                : null;
        }

        private void RebuildSelf(string navigationPath, NavigationMode navigationMode)
        {
            owner.SetNavigationTarget(navigationPath, navigationMode);
            owner.PreserveScrollAndRun(() =>
            {
                VisualElement previousRoot = root;
                VisualElement parent = previousRoot?.parent;
                int childIndex = parent != null ? parent.IndexOf(previousRoot) : -1;

                Build();

                if (parent == null || childIndex < 0)
                {
                    return;
                }

                previousRoot.RemoveFromHierarchy();
                parent.Insert(Mathf.Min(childIndex, parent.childCount), root);
            });
        }

        private void Build()
        {
            SerializedProperty effectProperty = GetEffectProperty();
            if (effectProperty == null)
            {
                root = new Box();
                root.style.display = DisplayStyle.None;
                return;
            }

            if (!string.IsNullOrEmpty(currentPath))
            {
                owner.RemoveLocalTargetsUnder(currentPath);
            }

            currentPath = effectProperty.propertyPath;
            bool expanded = owner.shouldExpandAll() || !owner.expansionState.TryGetValue(currentPath, out bool currentExpanded) || currentExpanded;

            Box container = new();
            container.style.paddingBottom = 10;
            container.style.paddingLeft = 10 + depth * 8;
            container.style.paddingRight = 10;
            container.style.paddingTop = 10;
            container.style.marginBottom = 6;
            container.style.backgroundColor = depth % 2 == 0
                ? new Color(0.16f, 0.18f, 0.21f)
                : new Color(0.19f, 0.21f, 0.24f);
            container.style.borderBottomLeftRadius = 8;
            container.style.borderBottomRightRadius = 8;
            container.style.borderTopLeftRadius = 8;
            container.style.borderTopRightRadius = 8;
            owner.RegisterTarget(currentPath, container);

            VisualElement header = new();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            container.Add(header);

            Button toggleButton = new(() =>
            {
                owner.expansionState[currentPath] = !expanded;
                RebuildSelf(currentPath, NavigationMode.FocusOnly);
            })
            {
                text = expanded ? "收起" : "展开"
            };
            toggleButton.style.minWidth = 70;
            header.Add(toggleButton);

            Label title = new(GetEffectTitle(effectIndex, effectProperty));
            title.style.flexGrow = 1;
            title.style.marginLeft = 8;
            header.Add(title);

            VisualElement actionRow = new();
            actionRow.style.flexDirection = FlexDirection.Row;
            header.Add(actionRow);

            actionRow.Add(CreateActionButton("上移", () =>
            {
                if (effectIndex <= 0)
                {
                    return;
                }

                owner.applyChange("Move Effect Up", () =>
                {
                    parentArrayProperty.MoveArrayElement(effectIndex, effectIndex - 1);
                    string movedPath = parentArrayProperty.GetArrayElementAtIndex(effectIndex - 1).propertyPath;
                    owner.SetNavigationTarget(movedPath, NavigationMode.FocusOnly);
                    owner.setPendingNavigation?.Invoke(movedPath);
                });
                rebuildScope(parentArrayProperty.propertyPath, NavigationMode.FocusOnly);
            }, false));

            actionRow.Add(CreateActionButton("下移", () =>
            {
                if (effectIndex >= parentArrayProperty.arraySize - 1)
                {
                    return;
                }

                owner.applyChange("Move Effect Down", () =>
                {
                    parentArrayProperty.MoveArrayElement(effectIndex, effectIndex + 1);
                    string movedPath = parentArrayProperty.GetArrayElementAtIndex(effectIndex + 1).propertyPath;
                    owner.SetNavigationTarget(movedPath, NavigationMode.FocusOnly);
                    owner.setPendingNavigation?.Invoke(movedPath);
                });
                rebuildScope(parentArrayProperty.propertyPath, NavigationMode.FocusOnly);
            }, true));

            actionRow.Add(CreateActionButton("删除", () =>
            {
                owner.applyChange("Delete Effect", () =>
                {
                    parentArrayProperty.DeleteArrayElementAtIndex(effectIndex);
                    owner.SetNavigationTarget(parentArrayProperty.propertyPath, NavigationMode.FocusOnly);
                    owner.setPendingNavigation?.Invoke(parentArrayProperty.propertyPath);
                });
                rebuildScope(parentArrayProperty.propertyPath, NavigationMode.FocusOnly);
            }, true));

            VisualElement content = new();
            content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            content.style.marginTop = 8;
            content.style.flexDirection = FlexDirection.Column;
            container.Add(content);

            SerializedProperty triggerProperty = effectProperty.FindPropertyRelative("triggerType");
            int triggerIndex = isTopLevelSpell && depth == 0 ? (int)TriggerType.None : triggerProperty.enumValueIndex;
            PopupField<string> triggerField = new("触发器", EditorLabelUtility.GetTriggerTypeLabels(), triggerIndex);
            triggerField.SetEnabled(!(isTopLevelSpell && depth == 0));
            triggerField.RegisterValueChangedCallback(_ =>
            {
                owner.applyChange("Update Trigger Type", () =>
                {
                    triggerProperty.enumValueIndex = triggerField.index;
                });
                owner.setPendingNavigation?.Invoke($"{currentPath}.triggerType");
                title.text = GetEffectTitle(effectIndex, GetEffectProperty());
            });
            owner.RegisterTarget($"{currentPath}.triggerType", triggerField);
            content.Add(triggerField);

            BuildConditionList(content, effectProperty.FindPropertyRelative("conditionTypes"));

            SerializedProperty effectTypeProperty = effectProperty.FindPropertyRelative("effectType");
            EffectType currentEffectType = EffectRegistry.GetEffectTypeAt(effectTypeProperty.enumValueIndex);
            EffectTargetMode currentMode = (EffectTargetMode)effectProperty.FindPropertyRelative("targetMode").enumValueIndex;
            VisualElement effectTypeRow = new();
            effectTypeRow.style.flexDirection = FlexDirection.Row;
            effectTypeRow.style.alignItems = Align.Center;
            Label effectTypeLabel = new("效果类型");
            effectTypeLabel.style.minWidth = 120;
            effectTypeRow.Add(effectTypeLabel);
            ToolbarMenu effectTypeField = new() { text = EffectEditorCatalog.GetDisplayLabel(currentEffectType, currentMode) };
            effectTypeField.style.flexGrow = 1;
            foreach (EffectEditorOption option in EffectEditorCatalog.GetOptions())
            {
                string path = option.Section == EffectEditorSection.None
                    ? option.Label
                    : $"{EffectEditorCatalog.GetSectionLabel(option.Section)}/{option.Label}";
                effectTypeField.menu.AppendAction(path, _ =>
                {
                    owner.applyChange("Update Effect Type", () =>
                    {
                        effectTypeProperty.enumValueIndex = EffectRegistry.GetLabelIndex(option.EffectType);
                        effectProperty.FindPropertyRelative("targetMode").enumValueIndex = (int)option.TargetMode;
                        effectProperty.FindPropertyRelative("targetSide").enumValueIndex = (int)EffectTargetSide.Friendly;
                        effectProperty.FindPropertyRelative("characterScope").enumValueIndex = (int)EffectCharacterScope.Minions;
                        effectProperty.FindPropertyRelative("includeSource").boolValue = true;
                        ResetEffectValuesForSchema(effectProperty.FindPropertyRelative("effectValues"), option.EffectType);
                    });
                    owner.setPendingNavigation?.Invoke($"{currentPath}.effectType");
                    RebuildSelf($"{currentPath}.effectType", NavigationMode.FocusOnly);
                });
            }
            effectTypeField.menu.AppendAction(
                $"{EffectEditorCatalog.GetSectionLabel(EffectEditorSection.Special)}/暂无可用效果",
                _ => { },
                _ => DropdownMenuAction.Status.Disabled);
            effectTypeRow.Add(effectTypeField);
            owner.RegisterTarget($"{currentPath}.effectType", effectTypeField);
            content.Add(effectTypeRow);

            BuildTargetConfigurationFields(content, effectProperty, currentEffectType);
            BuildEffectValueFields(content, effectProperty.FindPropertyRelative("effectValues"), currentEffectType);

            if (HasConditions(effectProperty) && HasBranchEffects(effectProperty))
            {
                content.Add(new HelpBox(
                    "当前节点配置了条件分支。按现有运行时逻辑，它会优先执行 then/else 分支，而不是先执行本节点效果。",
                    HelpBoxMessageType.Info));
            }

            content.Add(BuildBranchSection("满足分支", effectProperty.FindPropertyRelative("thenEffects")));
            content.Add(BuildBranchSection("否则分支", effectProperty.FindPropertyRelative("elseEffects")));

            root = container;
        }

        private void BuildConditionList(VisualElement parent, SerializedProperty conditionProperty)
        {
            string conditionPath = $"{currentPath}.conditionTypes";
            Box conditionBox = CreateInsetBox();
            conditionBox.style.width = Length.Percent(100);
            parent.Add(conditionBox);
            owner.RegisterTarget(conditionPath, conditionBox);

            VisualElement header = new();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            conditionBox.Add(header);

            header.Add(new Label("条件列表"));
            header.Add(new Button(() =>
            {
                owner.applyChange("Add Condition", () =>
                {
                    int newIndex = conditionProperty.arraySize;
                    conditionProperty.arraySize++;
                    SerializedProperty newCondition = conditionProperty.GetArrayElementAtIndex(newIndex);
                    newCondition.enumValueIndex = (int)ConditionType.None;
                    owner.setPendingNavigation?.Invoke(newCondition.propertyPath);
                });
                string newConditionPath = conditionProperty.GetArrayElementAtIndex(conditionProperty.arraySize - 1).propertyPath;
                RebuildSelf(newConditionPath, NavigationMode.FocusOnly);
            })
            {
                text = "新增"
            });

            if (conditionProperty.arraySize == 0)
            {
                Label emptyLabel = new("当前没有条件，默认直接执行。");
                emptyLabel.style.marginTop = 6;
                conditionBox.Add(emptyLabel);
                return;
            }

            for (int i = 0; i < conditionProperty.arraySize; i++)
            {
                int capturedIndex = i;
                SerializedProperty element = conditionProperty.GetArrayElementAtIndex(i);
                VisualElement row = new();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 6;
                row.style.width = Length.Percent(100);
                conditionBox.Add(row);

                PopupField<string> conditionField = new($"条件 {i + 1}", EditorLabelUtility.GetConditionTypeLabels(), element.enumValueIndex);
                conditionField.style.flexGrow = 1;
                conditionField.style.flexShrink = 1;
                conditionField.style.minWidth = 0;
                conditionField.style.marginRight = 6;
                conditionField.RegisterValueChangedCallback(_ =>
                {
                    owner.applyChange("Update Condition Type", () =>
                    {
                        element.enumValueIndex = conditionField.index;
                    });
                    owner.setPendingNavigation?.Invoke(element.propertyPath);
                    RebuildSelf(element.propertyPath, NavigationMode.FocusOnly);
                });
                owner.RegisterTarget(element.propertyPath, conditionField);
                row.Add(conditionField);

                Button deleteButton = new(() =>
                {
                    owner.applyChange("Delete Condition", () =>
                    {
                        conditionProperty.DeleteArrayElementAtIndex(capturedIndex);
                        owner.setPendingNavigation?.Invoke(conditionPath);
                    });
                    RebuildSelf(conditionPath, NavigationMode.FocusOnly);
                })
                {
                    text = "删除"
                };
                deleteButton.style.flexGrow = 0;
                deleteButton.style.flexShrink = 0;
                deleteButton.style.width = 56;
                row.Add(deleteButton);
            }
        }

        private VisualElement BuildBranchSection(string title, SerializedProperty branchArrayProperty)
        {
            string branchPath = branchArrayProperty.propertyPath;
            Box branchBox = CreateInsetBox();
            owner.RegisterTarget(branchPath, branchBox);

            VisualElement header = new();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            branchBox.Add(header);

            header.Add(new Label(title));
            header.Add(new Button(() =>
            {
                owner.applyChange("Add Branch Effect", () =>
                {
                    int newIndex = branchArrayProperty.arraySize;
                    branchArrayProperty.arraySize++;
                    SerializedProperty newEffect = branchArrayProperty.GetArrayElementAtIndex(newIndex);
                    InitializeEffectProperty(newEffect, false);
                    owner.SetNavigationTarget(newEffect.propertyPath, NavigationMode.ScrollToTarget);
                    owner.setPendingNavigation?.Invoke(newEffect.propertyPath);
                });
                RebuildSelf(branchPath, NavigationMode.ScrollToTarget);
            })
            {
                text = "新增子效果"
            });

            if (branchArrayProperty.arraySize == 0)
            {
                Label emptyLabel = new("当前分支为空。");
                emptyLabel.style.marginTop = 6;
                branchBox.Add(emptyLabel);
                return branchBox;
            }

            for (int i = 0; i < branchArrayProperty.arraySize; i++)
            {
                EffectCardView childCard = new(owner, branchArrayProperty, i, depth + 1, false, RebuildSelf);
                branchBox.Add(childCard.Root);
            }

            return branchBox;
        }

        private void BuildEffectValueFields(VisualElement parent, SerializedProperty effectValuesProperty, EffectType effectType)
        {
            ICardEffectDefinition definition = EffectRegistry.Get(effectType);
            if (definition.Parameters.Count == 0)
            {
                Label emptyLabel = new("当前效果没有额外参数。");
                emptyLabel.style.marginTop = 6;
                parent.Add(emptyLabel);
                return;
            }

            Box valueBox = CreateInsetBox();
            valueBox.Add(new Label("结构化参数"));
            owner.RegisterTarget($"{currentPath}.effectValues", valueBox);
            parent.Add(valueBox);

            foreach (EffectValueParameter parameter in definition.Parameters)
            {
                EffectTargetMode targetMode = (EffectTargetMode)GetEffectProperty().FindPropertyRelative("targetMode").enumValueIndex;
                if (!ShouldShowParameter(effectType, targetMode, parameter.Index))
                {
                    continue;
                }

                if (effectType == EffectType.SummonMinion && parameter.Index == 0)
                {
                    BuildMinionIdField(valueBox, effectValuesProperty, parameter);
                    continue;
                }

                IntegerField field = new(parameter.Label);
                field.value = GetEffectValue(effectValuesProperty, parameter.Index, parameter.DefaultValue);
                field.style.marginTop = 6;
                field.RegisterValueChangedCallback(evt =>
                {
                    owner.applyChange("Update Effect Values", () =>
                    {
                        EnsureArrayLength(effectValuesProperty, parameter.Index + 1);
                        effectValuesProperty.GetArrayElementAtIndex(parameter.Index).intValue = evt.newValue;
                    });
                    owner.setPendingNavigation?.Invoke($"{currentPath}.effectValues[{parameter.Index}]");
                });
                owner.RegisterTarget($"{currentPath}.effectValues[{parameter.Index}]", field);
                valueBox.Add(field);
            }
        }

        private void BuildTargetConfigurationFields(VisualElement parent, SerializedProperty effectProperty, EffectType effectType)
        {
            if (!EffectEditorCatalog.HasTargetConfiguration(effectType))
            {
                return;
            }

            Box box = CreateInsetBox();
            box.Add(new Label("目标配置"));
            parent.Add(box);

            SerializedProperty modeProperty = effectProperty.FindPropertyRelative("targetMode");
            IReadOnlyList<EffectTargetMode> modes = EffectEditorCatalog.GetModes(effectType);
            if (modes.Count > 1)
            {
                List<string> labels = new();
                int selectedIndex = 0;
                for (int i = 0; i < modes.Count; i++)
                {
                    labels.Add(GetTargetModeLabel(modes[i]));
                    if ((int)modes[i] == modeProperty.enumValueIndex) selectedIndex = i;
                }
                PopupField<string> modeField = new("选择方式", labels, selectedIndex);
                modeField.RegisterValueChangedCallback(_ =>
                {
                    owner.applyChange("Update Target Mode", () => modeProperty.enumValueIndex = (int)modes[modeField.index]);
                    RebuildSelf($"{currentPath}.targetMode", NavigationMode.FocusOnly);
                });
                box.Add(modeField);
            }

            EffectTargetMode mode = (EffectTargetMode)modeProperty.enumValueIndex;
            if (EffectEditorCatalog.UsesTargetSide(effectType) && mode != EffectTargetMode.Self)
            {
                AddEnumPopup(box, "作用方", effectProperty.FindPropertyRelative("targetSide"), new List<string> { "友方", "敌方", "双方" });
            }

            if (EffectEditorCatalog.UsesCharacterScope(effectType))
            {
                AddEnumPopup(box, "角色范围", effectProperty.FindPropertyRelative("characterScope"), new List<string> { "随从", "英雄", "全部角色" });
            }

            if (EffectEditorCatalog.UsesIncludeSource(effectType, mode))
            {
                SerializedProperty includeProperty = effectProperty.FindPropertyRelative("includeSource");
                Toggle toggle = new("包含效果来源") { value = includeProperty.boolValue };
                toggle.RegisterValueChangedCallback(evt => owner.applyChange("Update Include Source", () => includeProperty.boolValue = evt.newValue));
                box.Add(toggle);
            }
        }

        private void AddEnumPopup(VisualElement parent, string label, SerializedProperty property, List<string> labels)
        {
            PopupField<string> field = new(label, labels, Mathf.Clamp(property.enumValueIndex, 0, labels.Count - 1));
            field.RegisterValueChangedCallback(_ =>
            {
                owner.applyChange("Update Effect Target Configuration", () => property.enumValueIndex = field.index);
                owner.setPendingNavigation?.Invoke(property.propertyPath);
            });
            owner.RegisterTarget(property.propertyPath, field);
            parent.Add(field);
        }

        private void BuildMinionIdField(Box parent, SerializedProperty valuesProperty, EffectValueParameter parameter)
        {
            EnsureArrayLength(valuesProperty, parameter.Index + 1);
            int currentId = valuesProperty.GetArrayElementAtIndex(parameter.Index).intValue;
            List<CardData> minions = new();
            List<string> labels = new();
            int selectedIndex = 0;
            if (owner.database != null && owner.database.cards != null)
            {
                foreach (CardData card in owner.database.cards)
                {
                    if (card == null || card.cardType != CardType.Minion) continue;
                    if (card.index == currentId) selectedIndex = labels.Count;
                    minions.Add(card);
                    labels.Add($"{card.index} | {card.name} | {card.cost}费");
                }
            }

            if (labels.Count == 0)
            {
                parent.Add(new HelpBox("卡牌数据库中没有可召唤的随从。", HelpBoxMessageType.Warning));
                return;
            }

            PopupField<string> field = new(parameter.Label, labels, Mathf.Clamp(selectedIndex, 0, labels.Count - 1));
            field.RegisterValueChangedCallback(_ =>
            {
                owner.applyChange("Update Summon Minion ID", () => valuesProperty.GetArrayElementAtIndex(parameter.Index).intValue = minions[field.index].index);
                owner.setPendingNavigation?.Invoke($"{currentPath}.effectValues[{parameter.Index}]");
            });
            parent.Add(field);
        }

        private static bool ShouldShowParameter(EffectType effectType, EffectTargetMode mode, int parameterIndex)
        {
            if ((effectType == EffectType.Damage || effectType == EffectType.Heal) && parameterIndex == 1)
                return mode is EffectTargetMode.Selected or EffectTargetMode.Random;
            if (effectType == EffectType.Buff && parameterIndex == 2)
                return mode is EffectTargetMode.Selected or EffectTargetMode.Random;
            if ((effectType == EffectType.Destroy || effectType == EffectType.BackHand) && parameterIndex == 0)
                return mode is EffectTargetMode.Selected or EffectTargetMode.Random;
            if (effectType == EffectType.Silence && parameterIndex == 0)
                return mode is EffectTargetMode.Selected or EffectTargetMode.Random;
            return true;
        }

        private static string GetTargetModeLabel(EffectTargetMode mode)
        {
            return mode switch
            {
                EffectTargetMode.Self => "自身",
                EffectTargetMode.All => "全体",
                EffectTargetMode.Selected => "需要指定",
                EffectTargetMode.Random => "随机",
                _ => "未定义",
            };
        }
    }
}
