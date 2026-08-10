using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class CardDatabaseEditorWindow : EditorWindow
{
    private const string DefaultDatabasePath = "Assets/Resources/ArkCardsDatabase.asset";

    private readonly Dictionary<string, bool> effectExpansionState = new();
    private readonly Dictionary<string, VisualElement> navigationTargets = new();
    private readonly Dictionary<int, Button> cardRowButtons = new();
    private readonly List<CardListEntry> filteredEntries = new();

    private CardListSO database;
    private SerializedObject serializedDatabase;
    private int selectedCardIndex = -1;
    private string searchText = string.Empty;
    private CardFilterOption filterOption = CardFilterOption.All;
    private CardSortOption sortOption = CardSortOption.IdAscending;
    private bool expandAllEffectsOnNextRefresh;
    private string pendingNavigationPath;

    private bool effectFoldoutExpanded = true;
    private bool passiveFoldoutExpanded = true;
    private bool validationFoldoutExpanded = true;
    private bool previewFoldoutExpanded = true;
    private bool compatibilityFoldoutExpanded;
    private bool aiFoldoutExpanded = true;

    private ObjectField databaseField;
    private Label assetPathLabel;
    private TextField searchFieldControl;
    private PopupField<string> filterFieldControl;
    private VisualElement leftPanel;
    private ScrollView cardListScrollView;
    private Button newMinionButton;
    private Button newSpellButton;
    private Button duplicateButton;
    private Button deleteButton;
    private Button sortButton;
    private Button saveButton;

    private VisualElement middlePanel;
    private ScrollView middleScrollView;
    private Foldout validationFoldoutElement;
    private CardPreviewElement previewElement;
    private VisualElement rightPanel;
    private ScrollView rightScrollView;

    [MenuItem("Tools/ArkCards/Card Editor")]
    public static void Open()
    {
        CardDatabaseEditorWindow window = GetWindow<CardDatabaseEditorWindow>();
        window.titleContent = new GUIContent("Card Editor");
        window.minSize = new Vector2(1400f, 720f);
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += HandleUndoRedo;
        TryLoadDefaultDatabase();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
    }

    private void CreateGUI()
    {
        BuildWindowShell();
        RefreshAllPanels(true, true, true, true);
    }

    private void HandleUndoRedo()
    {
        RefreshAllPanels(true, true, true, false);
    }

    private void BuildWindowShell()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.flexDirection = FlexDirection.Column;
        rootVisualElement.style.flexGrow = 1;
        rootVisualElement.style.paddingBottom = 8;
        rootVisualElement.style.paddingLeft = 8;
        rootVisualElement.style.paddingRight = 8;
        rootVisualElement.style.paddingTop = 8;

        rootVisualElement.Add(BuildHeader());

        TwoPaneSplitView outerSplit = new(0, 280, TwoPaneSplitViewOrientation.Horizontal);
        outerSplit.style.flexGrow = 1;
        rootVisualElement.Add(outerSplit);

        leftPanel = CreatePanelContainer();
        leftPanel.style.width = 280;
        outerSplit.Add(leftPanel);

        // 详情面板固定为可编辑区域总宽度的 1/3，效果编辑区占剩余 2/3，分隔条可拖动。
        float detailsPanelWidth = Mathf.Max(300f, (position.width - 296f) / 3f);
        TwoPaneSplitView rightSplit = new(1, detailsPanelWidth, TwoPaneSplitViewOrientation.Horizontal);
        rightSplit.style.flexGrow = 1;
        outerSplit.Add(rightSplit);

        middlePanel = new VisualElement();
        middlePanel.style.flexGrow = 1;
        rightSplit.Add(middlePanel);

        rightPanel = new VisualElement();
        rightPanel.style.flexGrow = 1;
        rightSplit.Add(rightPanel);

        BuildLeftPanel();
    }

    private VisualElement BuildHeader()
    {
        VisualElement header = new();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.marginBottom = 8;

        Label title = new("ArkCards 数据库");
        title.style.minWidth = 130;
        title.style.marginRight = 8;
        header.Add(title);

        databaseField = new ObjectField
        {
            objectType = typeof(CardListSO),
            allowSceneObjects = false,
        };
        databaseField.style.flexGrow = 1;
        databaseField.style.marginRight = 8;
        databaseField.RegisterValueChangedCallback(evt =>
        {
            database = evt.newValue as CardListSO;
            CardEffectMigrationService.MigrateIfNeeded(database);
            RecreateSerializedDatabase();
            selectedCardIndex = database != null && database.cards != null && database.cards.Count > 0 ? 0 : -1;
            effectExpansionState.Clear();
            pendingNavigationPath = null;
            RefreshAllPanels(true, true, true, true);
        });
        header.Add(databaseField);

        assetPathLabel = new Label();
        assetPathLabel.style.color = new Color(0.67f, 0.71f, 0.75f);
        header.Add(assetPathLabel);
        return header;
    }

    private void BuildLeftPanel()
    {
        leftPanel.Clear();
        cardRowButtons.Clear();

        leftPanel.Add(BuildSectionTitle("卡牌列表"));

        VisualElement buttonRow1 = CreateButtonRow();
        newMinionButton = CreateActionButton("新建随从", () => AddCard(CardType.Minion));
        newSpellButton = CreateActionButton("新建法术", () => AddCard(CardType.SPELL));
        buttonRow1.Add(newMinionButton);
        buttonRow1.Add(newSpellButton);
        leftPanel.Add(buttonRow1);

        VisualElement buttonRow2 = CreateButtonRow();
        duplicateButton = CreateActionButton("复制", DuplicateSelectedCard);
        deleteButton = CreateActionButton("删除", DeleteSelectedCard);
        buttonRow2.Add(duplicateButton);
        buttonRow2.Add(deleteButton);
        leftPanel.Add(buttonRow2);

        VisualElement buttonRow3 = CreateButtonRow();
        sortButton = CreateActionButton("按 ID 重排", SortByIdInPlace);
        saveButton = CreateActionButton("保存", SaveDatabase);
        buttonRow3.Add(sortButton);
        buttonRow3.Add(saveButton);
        leftPanel.Add(buttonRow3);

        searchFieldControl = new TextField("搜索");
        searchFieldControl.value = searchText;
        searchFieldControl.RegisterValueChangedCallback(evt =>
        {
            searchText = evt.newValue ?? string.Empty;
            RefreshCardList(true, false);
        });
        leftPanel.Add(searchFieldControl);

        filterFieldControl = new PopupField<string>("类型筛选", new List<string> { "全部", "随从", "法术" }, (int)filterOption);
        filterFieldControl.RegisterValueChangedCallback(evt =>
        {
            filterOption = evt.newValue switch
            {
                "随从" => CardFilterOption.Minion,
                "法术" => CardFilterOption.Spell,
                _ => CardFilterOption.All,
            };
            RefreshCardList(true, false);
        });
        leftPanel.Add(filterFieldControl);

        PopupField<string> sortField = new("列表排序", new List<string>
        {
            "ID 升序",
            "ID 降序",
            "费用 升序",
            "费用 降序",
            "名称 A-Z",
        }, (int)sortOption);
        sortField.RegisterValueChangedCallback(evt =>
        {
            sortOption = evt.newValue switch
            {
                "ID 降序" => CardSortOption.IdDescending,
                "费用 升序" => CardSortOption.CostAscending,
                "费用 降序" => CardSortOption.CostDescending,
                "名称 A-Z" => CardSortOption.NameAscending,
                _ => CardSortOption.IdAscending,
            };
            RefreshCardList(true, false);
        });
        leftPanel.Add(sortField);

        cardListScrollView = new ScrollView();
        cardListScrollView.style.flexGrow = 1;
        cardListScrollView.style.marginTop = 8;
        leftPanel.Add(cardListScrollView);
    }

    private void RefreshAllPanels(bool refreshList, bool refreshMiddle, bool refreshRight, bool focusSelectedRow)
    {
        TryLoadDefaultDatabase();
        UpdateHeaderState();

        if (database == null)
        {
            RefreshCardList(true, false);
            RefreshMiddlePanel();
            RefreshRightPanel();
            return;
        }

        RecreateSerializedDatabase();
        ClampSelection();
        UpdateToolbarState();

        if (refreshList)
        {
            RefreshCardList(true, focusSelectedRow);
        }
        else
        {
            RefreshCardRowStyles();
        }

        if (refreshMiddle)
        {
            RefreshMiddlePanel();
        }

        if (refreshRight)
        {
            RefreshRightPanel();
        }
    }

    private void RefreshCardList(bool rebuildRows, bool focusSelectedRow)
    {
        if (cardListScrollView == null)
        {
            return;
        }

        if (database == null || database.cards == null)
        {
            cardListScrollView.Clear();
            cardListScrollView.Add(new HelpBox("未找到默认卡牌数据库，请在上方选择一个 CardListSO 资产。", HelpBoxMessageType.Warning));
            cardRowButtons.Clear();
            return;
        }

        BuildVisibleEntries();
        if (!rebuildRows)
        {
            RefreshCardRowStyles();
            if (focusSelectedRow)
            {
                FocusSelectedCardRow();
            }
            return;
        }

        Vector2 previousScroll = cardListScrollView.scrollOffset;
        cardListScrollView.Clear();
        cardRowButtons.Clear();

        if (filteredEntries.Count == 0)
        {
            cardListScrollView.Add(new Label("没有匹配的卡牌。"));
            return;
        }

        foreach (CardListEntry entry in filteredEntries)
        {
            Button row = BuildCardListRow(entry);
            cardRowButtons[entry.ActualIndex] = row;
            cardListScrollView.Add(row);
        }

        RefreshCardRowStyles();
        if (focusSelectedRow)
        {
            FocusSelectedCardRow();
        }
        else
        {
            cardListScrollView.schedule.Execute(() => cardListScrollView.scrollOffset = previousScroll).ExecuteLater(0);
        }
    }

    private void RefreshMiddlePanel()
    {
        if (middlePanel == null)
        {
            return;
        }

        Vector2 scrollOffset = middleScrollView != null ? middleScrollView.scrollOffset : Vector2.zero;
        middlePanel.Clear();
        navigationTargets.Clear();

        middleScrollView = new ScrollView();
        middleScrollView.style.flexGrow = 1;
        middleScrollView.style.marginLeft = 8;
        middleScrollView.style.marginRight = 8;
        middleScrollView.style.paddingBottom = 8;
        middleScrollView.style.paddingLeft = 8;
        middleScrollView.style.paddingRight = 8;
        middleScrollView.style.paddingTop = 8;
        middlePanel.Add(middleScrollView);

        middleScrollView.Add(BuildSectionTitle("被动 / 效果 / 校验 / 预览"));

        if (database == null)
        {
            previewElement = null;
            middleScrollView.Add(new HelpBox("请先选择卡牌数据库。", HelpBoxMessageType.Info));
            return;
        }

        if (!HasSelection())
        {
            previewElement = null;
            middleScrollView.Add(new HelpBox("请选择一张卡牌后再编辑效果。", HelpBoxMessageType.Info));
            return;
        }

        SerializedProperty cardProperty = GetSelectedCardProperty();
        CardData selectedCard = GetSelectedCard();
        if (cardProperty == null || selectedCard == null)
        {
            previewElement = null;
            middleScrollView.Add(new HelpBox("当前卡牌数据读取失败。", HelpBoxMessageType.Error));
            return;
        }

        Foldout passiveFoldout = new() { text = "被动配置", value = passiveFoldoutExpanded };
        passiveFoldout.RegisterValueChangedCallback(evt => passiveFoldoutExpanded = evt.newValue);
        passiveFoldout.Add(new CardPassiveEditorElement(
            cardProperty.FindPropertyRelative("passiveTypes"),
            ApplyEffectChange,
            SetPendingEffectNavigation,
            RegisterNavigationTarget));
        middleScrollView.Add(passiveFoldout);

        Foldout effectFoldout = new() { text = "效果编辑区", value = effectFoldoutExpanded };
        effectFoldout.RegisterValueChangedCallback(evt => effectFoldoutExpanded = evt.newValue);
        effectFoldout.Add(new CardEffectEditorElement(
            cardProperty.FindPropertyRelative("effects"),
            selectedCard.cardType,
            database,
            ApplyEffectChange,
            RefreshMiddlePanel,
            SetPendingEffectNavigation,
            RegisterNavigationTarget,
            effectExpansionState,
            () => expandAllEffectsOnNextRefresh));
        middleScrollView.Add(effectFoldout);

        validationFoldoutElement = new Foldout { text = "校验面板", value = validationFoldoutExpanded };
        validationFoldoutElement.RegisterValueChangedCallback(evt => validationFoldoutExpanded = evt.newValue);
        middleScrollView.Add(validationFoldoutElement);
        RefreshValidationPanelOnly();

        Foldout previewFoldout = new() { text = "卡面预览", value = previewFoldoutExpanded };
        previewFoldout.RegisterValueChangedCallback(evt => previewFoldoutExpanded = evt.newValue);
        previewElement = new CardPreviewElement();
        previewElement.SetCard(selectedCard);
        previewFoldout.Add(previewElement);
        middleScrollView.Add(previewFoldout);

        middleScrollView.schedule.Execute(() =>
        {
            middleScrollView.scrollOffset = scrollOffset;
            NavigateToPendingPath();
        }).ExecuteLater(0);
    }

    private void RefreshRightPanel()
    {
        if (rightPanel == null)
        {
            return;
        }

        Vector2 scrollOffset = rightScrollView != null ? rightScrollView.scrollOffset : Vector2.zero;
        rightPanel.Clear();

        rightScrollView = new ScrollView();
        rightScrollView.style.flexGrow = 1;
        rightScrollView.style.paddingBottom = 8;
        rightScrollView.style.paddingLeft = 8;
        rightScrollView.style.paddingRight = 8;
        rightScrollView.style.paddingTop = 8;
        rightPanel.Add(rightScrollView);

        rightScrollView.Add(BuildSectionTitle("详情表单"));

        if (database == null)
        {
            rightScrollView.Add(new HelpBox("请先选择卡牌数据库。", HelpBoxMessageType.Info));
            return;
        }

        if (!HasSelection())
        {
            rightScrollView.Add(new HelpBox("当前数据库中还没有卡牌，请先新建一张。", HelpBoxMessageType.Info));
            return;
        }

        SerializedProperty cardProperty = GetSelectedCardProperty();
        if (cardProperty == null)
        {
            rightScrollView.Add(new HelpBox("当前卡牌属性读取失败。", HelpBoxMessageType.Error));
            return;
        }

        CardType cardType = (CardType)cardProperty.FindPropertyRelative("cardType").enumValueIndex;
        string cardPath = CardValidationService.GetCardPropertyPath(selectedCardIndex);

        rightScrollView.Add(CreateIntegerField("ID", $"{cardPath}.index", cardProperty.FindPropertyRelative("index"), refreshList: true, refreshMiddle: false));
        rightScrollView.Add(CreateTextField("名称", $"{cardPath}.name", cardProperty.FindPropertyRelative("name"), false, refreshList: true, refreshMiddle: false));

        PopupField<string> cardTypeField = new("类型", EditorLabelUtility.GetCardTypeLabels(), (int)cardType);
        cardTypeField.RegisterValueChangedCallback(evt =>
        {
            ApplySerializedChange("Update Card Type", () =>
            {
                cardProperty.FindPropertyRelative("cardType").enumValueIndex = cardTypeField.index;
            }, refreshList: true, refreshMiddle: true, refreshRight: true, focusSelectedRow: false);
        });
        RegisterNavigationTarget($"{cardPath}.cardType", cardTypeField);
        rightScrollView.Add(cardTypeField);

        rightScrollView.Add(CreateIntegerField("费用", $"{cardPath}.cost", cardProperty.FindPropertyRelative("cost"), refreshList: true, refreshMiddle: false));

        if (cardType == CardType.Minion)
        {
            rightScrollView.Add(CreateIntegerField("攻击", $"{cardPath}.attack", cardProperty.FindPropertyRelative("attack"), refreshList: false, refreshMiddle: false));
            rightScrollView.Add(CreateIntegerField("生命", $"{cardPath}.health", cardProperty.FindPropertyRelative("health"), refreshList: false, refreshMiddle: false));
        }
        else
        {
            Foldout compatibilityFoldout = new() { text = "高级 / 兼容字段", value = compatibilityFoldoutExpanded };
            compatibilityFoldout.RegisterValueChangedCallback(evt => compatibilityFoldoutExpanded = evt.newValue);
            compatibilityFoldout.Add(CreateIntegerField("攻击", $"{cardPath}.attack", cardProperty.FindPropertyRelative("attack"), refreshList: false, refreshMiddle: false));
            compatibilityFoldout.Add(CreateIntegerField("生命", $"{cardPath}.health", cardProperty.FindPropertyRelative("health"), refreshList: false, refreshMiddle: false));
            rightScrollView.Add(compatibilityFoldout);
        }

        Foldout aiFoldout = new() { text = "AI 配置", value = aiFoldoutExpanded };
        aiFoldout.RegisterValueChangedCallback(evt => aiFoldoutExpanded = evt.newValue);

        PopupField<string> aiRoleField = new("定位", EditorLabelUtility.GetCardAIRoleLabels(), cardProperty.FindPropertyRelative("aiRole").enumValueIndex);
        aiRoleField.RegisterValueChangedCallback(evt => ApplySerializedChange("Update AI Role", () =>
        {
            cardProperty.FindPropertyRelative("aiRole").enumValueIndex = aiRoleField.index;
        }, refreshList: false, refreshMiddle: true, refreshRight: false, focusSelectedRow: false));
        RegisterNavigationTarget($"{cardPath}.aiRole", aiRoleField);
        aiFoldout.Add(aiRoleField);

        PopupField<string> aiStyleField = new("打法", EditorLabelUtility.GetAIPlayStyleLabels(), cardProperty.FindPropertyRelative("aiPlayStyle").enumValueIndex);
        aiStyleField.RegisterValueChangedCallback(evt => ApplySerializedChange("Update AI Play Style", () =>
        {
            cardProperty.FindPropertyRelative("aiPlayStyle").enumValueIndex = aiStyleField.index;
        }, refreshList: false, refreshMiddle: true, refreshRight: false, focusSelectedRow: false));
        RegisterNavigationTarget($"{cardPath}.aiPlayStyle", aiStyleField);
        aiFoldout.Add(aiStyleField);

        PopupField<string> aiTargetField = new("目标偏好", EditorLabelUtility.GetAITargetPriorityLabels(), cardProperty.FindPropertyRelative("aiTargetPriority").enumValueIndex);
        aiTargetField.RegisterValueChangedCallback(evt => ApplySerializedChange("Update AI Target Priority", () =>
        {
            cardProperty.FindPropertyRelative("aiTargetPriority").enumValueIndex = aiTargetField.index;
        }, refreshList: false, refreshMiddle: true, refreshRight: false, focusSelectedRow: false));
        RegisterNavigationTarget($"{cardPath}.aiTargetPriority", aiTargetField);
        aiFoldout.Add(aiTargetField);

        aiFoldout.Add(CreateIntegerField("基础优先级", $"{cardPath}.aiBasePriority", cardProperty.FindPropertyRelative("aiBasePriority"), refreshList: false, refreshMiddle: true));
        aiFoldout.Add(CreateIntegerField("连携保留阈值", $"{cardPath}.aiComboReserveThreshold", cardProperty.FindPropertyRelative("aiComboReserveThreshold"), refreshList: false, refreshMiddle: true));
        aiFoldout.Add(CreateIntegerField("斩杀加成", $"{cardPath}.aiLethalBonus", cardProperty.FindPropertyRelative("aiLethalBonus"), refreshList: false, refreshMiddle: true));
        rightScrollView.Add(aiFoldout);

        rightScrollView.Add(CreateObjectField("图片", typeof(Sprite), $"{cardPath}.image", cardProperty.FindPropertyRelative("image"), refreshMiddle: false));
        rightScrollView.Add(CreateObjectField("攻击音效", typeof(AudioClip), $"{cardPath}.attackAudio", cardProperty.FindPropertyRelative("attackAudio"), refreshMiddle: false));
        rightScrollView.Add(CreateObjectField("入场音效", typeof(AudioClip), $"{cardPath}.enterAudio", cardProperty.FindPropertyRelative("enterAudio"), refreshMiddle: false));
        rightScrollView.Add(CreateTextField("描述", $"{cardPath}.effectDescription", cardProperty.FindPropertyRelative("effectDescription"), true, refreshList: false, refreshMiddle: false));

        rightScrollView.schedule.Execute(() => rightScrollView.scrollOffset = scrollOffset).ExecuteLater(0);
    }

    private VisualElement BuildValidationPanel()
    {
        VisualElement container = new();
        container.style.flexDirection = FlexDirection.Column;

        List<CardValidationMessage> messages = CardValidationService.Validate(database, selectedCardIndex);
        if (messages.Count == 0)
        {
            container.Add(new HelpBox("当前卡牌未发现问题。", HelpBoxMessageType.Info));
            return container;
        }

        foreach (CardValidationMessage message in messages)
        {
            Button button = new(() =>
            {
                selectedCardIndex = message.CardIndex;
                pendingNavigationPath = message.PropertyPath;
                if (!string.IsNullOrEmpty(message.PropertyPath) && message.PropertyPath.Contains(".effects."))
                {
                    expandAllEffectsOnNextRefresh = true;
                }

                RefreshCardRowStyles();
                FocusSelectedCardRow();
                RefreshMiddlePanel();
                RefreshRightPanel();
            })
            {
                text = $"[{GetSeverityLabel(message.Severity)}] {message.Message}"
            };
            button.style.marginBottom = 6;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.minHeight = 36;
            button.style.paddingBottom = 8;
            button.style.paddingTop = 8;
            button.style.backgroundColor = GetSeverityColor(message.Severity);
            container.Add(button);
        }

        return container;
    }

    private Button BuildCardListRow(CardListEntry entry)
    {
        Button row = new(() =>
        {
            selectedCardIndex = entry.ActualIndex;
            RefreshCardRowStyles();
            FocusSelectedCardRow();
            RefreshMiddlePanel();
            RefreshRightPanel();
        })
        {
            text = FormatCardListText(entry.Card)
        };
        row.userData = entry.ActualIndex;
        row.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.style.marginBottom = 4;
        row.style.whiteSpace = WhiteSpace.Normal;
        row.style.height = 40;
        return row;
    }

    private static VisualElement CreatePanelContainer()
    {
        VisualElement container = new();
        container.style.flexGrow = 1;
        container.style.paddingBottom = 8;
        container.style.paddingLeft = 8;
        container.style.paddingRight = 8;
        container.style.paddingTop = 8;
        container.style.backgroundColor = new Color(0.11f, 0.12f, 0.14f);
        container.style.borderBottomLeftRadius = 8;
        container.style.borderBottomRightRadius = 8;
        container.style.borderTopLeftRadius = 8;
        container.style.borderTopRightRadius = 8;
        return container;
    }

    private static Label BuildSectionTitle(string text)
    {
        Label label = new(text);
        label.style.fontSize = 14;
        label.style.marginBottom = 8;
        return label;
    }

    private static VisualElement CreateButtonRow()
    {
        VisualElement row = new();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 6;
        return row;
    }

    private Button CreateActionButton(string text, Action action, bool enabled = true)
    {
        Button button = new(action) { text = text };
        button.style.flexGrow = 1;
        button.style.marginRight = 6;
        button.SetEnabled(enabled);
        return button;
    }

    private IntegerField CreateIntegerField(string label, string propertyPath, SerializedProperty property, bool refreshList, bool refreshMiddle)
    {
        IntegerField field = new(label) { value = property.intValue };
        field.isDelayed = true;
        field.RegisterValueChangedCallback(evt =>
        {
            ApplySerializedChange($"Update {label}", () =>
            {
                property.intValue = evt.newValue;
            }, refreshList, refreshMiddle, refreshRight: false, focusSelectedRow: false);
        });
        RegisterNavigationTarget(propertyPath, field);
        return field;
    }

    private TextField CreateTextField(string label, string propertyPath, SerializedProperty property, bool multiline, bool refreshList, bool refreshMiddle)
    {
        TextField field = new(label)
        {
            value = property.stringValue,
            multiline = multiline
        };
        field.isDelayed = true;
        if (multiline)
        {
            field.style.minHeight = 96;
            field.style.whiteSpace = WhiteSpace.Normal;
        }

        field.RegisterValueChangedCallback(evt =>
        {
            ApplySerializedChange($"Update {label}", () =>
            {
                property.stringValue = evt.newValue ?? string.Empty;
            }, refreshList, refreshMiddle, refreshRight: false, focusSelectedRow: false);
        });
        RegisterNavigationTarget(propertyPath, field);
        return field;
    }

    private ObjectField CreateObjectField(string label, Type objectType, string propertyPath, SerializedProperty property, bool refreshMiddle)
    {
        ObjectField field = new(label)
        {
            objectType = objectType,
            allowSceneObjects = false,
            value = property.objectReferenceValue
        };
        field.RegisterValueChangedCallback(evt =>
        {
            ApplySerializedChange($"Update {label}", () =>
            {
                property.objectReferenceValue = evt.newValue as UnityEngine.Object;
            }, refreshList: false, refreshMiddle: refreshMiddle, refreshRight: false, focusSelectedRow: false);
        });
        RegisterNavigationTarget(propertyPath, field);
        return field;
    }

    private void AddCard(CardType cardType)
    {
        EnsureNewCardWillBeVisible();
        ApplyObjectChange(cardType == CardType.Minion ? "Add Minion Card" : "Add Spell Card", () =>
        {
            CardData card = CreateDefaultCard(cardType);
            database.cards.Add(card);
            selectedCardIndex = database.cards.Count - 1;
        }, refreshList: true, refreshMiddle: true, refreshRight: true, focusSelectedRow: true);
    }

    private void DuplicateSelectedCard()
    {
        if (!HasSelection())
        {
            return;
        }

        EnsureNewCardWillBeVisible();
        ApplyObjectChange("Duplicate Card", () =>
        {
            CardData clone = CloneCard(GetSelectedCard());
            clone.index = GetSuggestedId(clone.cardType);
            clone.name = $"{GetSafeCardName(clone)} 副本";
            int insertIndex = selectedCardIndex + 1;
            database.cards.Insert(insertIndex, clone);
            selectedCardIndex = insertIndex;
        }, refreshList: true, refreshMiddle: true, refreshRight: true, focusSelectedRow: true);
    }

    private void DeleteSelectedCard()
    {
        if (!HasSelection())
        {
            return;
        }

        CardData card = GetSelectedCard();
        if (!EditorUtility.DisplayDialog(
                "删除卡牌",
                $"确认删除卡牌“{GetSafeCardName(card)}”吗？",
                "删除",
                "取消"))
        {
            return;
        }

        ApplyObjectChange("Delete Card", () =>
        {
            database.cards.RemoveAt(selectedCardIndex);
            if (database.cards.Count == 0)
            {
                selectedCardIndex = -1;
            }
            else
            {
                selectedCardIndex = Mathf.Clamp(selectedCardIndex, 0, database.cards.Count - 1);
            }
        }, refreshList: true, refreshMiddle: true, refreshRight: true, focusSelectedRow: false);
    }

    private void SortByIdInPlace()
    {
        if (database == null || database.cards == null || database.cards.Count <= 1)
        {
            return;
        }

        ApplyObjectChange("Sort Cards By Id", () =>
        {
            CardData selectedCard = GetSelectedCard();
            List<CardData> sortedCards = database.cards.OrderBy(card => card.index).ToList();
            database.cards.Clear();
            database.cards.AddRange(sortedCards);
            selectedCardIndex = selectedCard != null ? database.cards.IndexOf(selectedCard) : -1;
        }, refreshList: true, refreshMiddle: true, refreshRight: true, focusSelectedRow: true);
    }

    private void SaveDatabase()
    {
        if (database == null)
        {
            return;
        }

        serializedDatabase?.ApplyModifiedProperties();
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent("卡牌数据库已保存"));
    }

    private void ApplyEffectChange(string undoName, Action change)
    {
        if (database == null)
        {
            return;
        }

        Undo.RecordObject(database, undoName);
        if (serializedDatabase == null)
        {
            serializedDatabase = new SerializedObject(database);
        }

        serializedDatabase.Update();
        change?.Invoke();
        serializedDatabase.ApplyModifiedProperties();
        EditorUtility.SetDirty(database);
        RefreshValidationPanelOnly();
        RefreshPreviewOnly();
    }

    private void SetPendingEffectNavigation(string propertyPath)
    {
        pendingNavigationPath = propertyPath;
    }

    private void RefreshValidationPanelOnly()
    {
        if (validationFoldoutElement == null)
        {
            return;
        }

        validationFoldoutElement.Clear();
        validationFoldoutElement.Add(BuildValidationPanel());
    }

    private void RefreshPreviewOnly()
    {
        if (previewElement == null)
        {
            return;
        }

        previewElement.SetCard(GetSelectedCard());
    }

    private void ApplySerializedChange(string undoName, Action change, bool refreshList, bool refreshMiddle, bool refreshRight, bool focusSelectedRow)
    {
        if (database == null)
        {
            return;
        }

        Undo.RecordObject(database, undoName);
        if (serializedDatabase == null)
        {
            serializedDatabase = new SerializedObject(database);
        }

        serializedDatabase.Update();
        change?.Invoke();
        serializedDatabase.ApplyModifiedProperties();
        EditorUtility.SetDirty(database);
        RefreshValidationPanelOnly();
        RefreshPreviewOnly();

        if (refreshList)
        {
            RefreshCardList(true, focusSelectedRow);
        }
        else
        {
            RefreshCardRowStyles();
        }

        if (focusSelectedRow)
        {
            FocusSelectedCardRow();
        }

        if (refreshMiddle)
        {
            RefreshMiddlePanel();
        }

        if (refreshRight)
        {
            RefreshRightPanel();
        }
    }

    private void ApplyObjectChange(string undoName, Action change, bool refreshList, bool refreshMiddle, bool refreshRight, bool focusSelectedRow)
    {
        if (database == null)
        {
            return;
        }

        Undo.RecordObject(database, undoName);
        change?.Invoke();
        EditorUtility.SetDirty(database);
        RecreateSerializedDatabase();
        ClampSelection();

        if (refreshList)
        {
            RefreshCardList(true, focusSelectedRow);
        }

        if (refreshMiddle)
        {
            RefreshMiddlePanel();
        }

        if (refreshRight)
        {
            RefreshRightPanel();
        }

        UpdateToolbarState();
    }

    private void RecreateSerializedDatabase()
    {
        serializedDatabase = database != null ? new SerializedObject(database) : null;
    }

    private void RegisterNavigationTarget(string path, VisualElement element)
    {
        if (string.IsNullOrEmpty(path) || element == null)
        {
            return;
        }

        navigationTargets[path] = element;
    }

    private void NavigateToPendingPath()
    {
        if (string.IsNullOrEmpty(pendingNavigationPath))
        {
            expandAllEffectsOnNextRefresh = false;
            return;
        }

        string lookupPath = pendingNavigationPath;
        while (!string.IsNullOrEmpty(lookupPath))
        {
            if (navigationTargets.TryGetValue(lookupPath, out VisualElement target))
            {
                ScrollView scrollView = target.GetFirstAncestorOfType<ScrollView>();
                scrollView?.ScrollTo(target);
                target.Focus();
                break;
            }

            int trimIndex = lookupPath.LastIndexOf('.');
            lookupPath = trimIndex > 0 ? lookupPath.Substring(0, trimIndex) : null;
        }

        pendingNavigationPath = null;
        expandAllEffectsOnNextRefresh = false;
    }

    private void UpdateHeaderState()
    {
        if (databaseField != null)
        {
            databaseField.SetValueWithoutNotify(database);
        }

        if (assetPathLabel != null)
        {
            assetPathLabel.text = database != null ? AssetDatabase.GetAssetPath(database) : DefaultDatabasePath;
        }

        searchFieldControl?.SetValueWithoutNotify(searchText);
        if (filterFieldControl != null)
        {
            filterFieldControl.index = (int)filterOption;
        }
    }

    private void UpdateToolbarState()
    {
        bool hasDatabase = database != null;
        bool hasSelection = HasSelection();
        newMinionButton?.SetEnabled(hasDatabase);
        newSpellButton?.SetEnabled(hasDatabase);
        duplicateButton?.SetEnabled(hasSelection);
        deleteButton?.SetEnabled(hasSelection);
        sortButton?.SetEnabled(hasDatabase && database.cards.Count > 1);
        saveButton?.SetEnabled(hasDatabase);
    }

    private SerializedProperty GetSelectedCardProperty()
    {
        return database != null && serializedDatabase != null && HasSelection()
            ? serializedDatabase.FindProperty(CardValidationService.GetCardPropertyPath(selectedCardIndex))
            : null;
    }

    private CardData GetSelectedCard()
    {
        return database != null && HasSelection() ? database.cards[selectedCardIndex] : null;
    }

    private bool HasSelection()
    {
        return database != null
            && database.cards != null
            && selectedCardIndex >= 0
            && selectedCardIndex < database.cards.Count;
    }

    private void TryLoadDefaultDatabase()
    {
        if (database != null)
        {
            return;
        }

        database = AssetDatabase.LoadAssetAtPath<CardListSO>(DefaultDatabasePath);
        CardEffectMigrationService.MigrateIfNeeded(database);
        RecreateSerializedDatabase();
        if (database != null && database.cards != null && database.cards.Count > 0 && selectedCardIndex < 0)
        {
            selectedCardIndex = 0;
        }
    }

    private void ClampSelection()
    {
        if (database == null || database.cards == null || database.cards.Count == 0)
        {
            selectedCardIndex = -1;
            return;
        }

        if (selectedCardIndex < 0 || selectedCardIndex >= database.cards.Count)
        {
            selectedCardIndex = Mathf.Clamp(selectedCardIndex, 0, database.cards.Count - 1);
        }
    }

    private void BuildVisibleEntries()
    {
        filteredEntries.Clear();
        if (database == null || database.cards == null)
        {
            return;
        }

        IEnumerable<CardListEntry> entries = Enumerable.Range(0, database.cards.Count)
            .Select(index => new CardListEntry(index, database.cards[index]));

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            entries = entries.Where(entry => IsContiguousSearchMatch(searchText, entry.Card));
        }

        entries = filterOption switch
        {
            CardFilterOption.Minion => entries.Where(entry => entry.Card.cardType == CardType.Minion),
            CardFilterOption.Spell => entries.Where(entry => entry.Card.cardType == CardType.SPELL),
            _ => entries,
        };

        entries = sortOption switch
        {
            CardSortOption.IdDescending => entries.OrderByDescending(entry => entry.Card.index),
            CardSortOption.CostAscending => entries.OrderBy(entry => entry.Card.cost).ThenBy(entry => entry.Card.index),
            CardSortOption.CostDescending => entries.OrderByDescending(entry => entry.Card.cost).ThenBy(entry => entry.Card.index),
            CardSortOption.NameAscending => entries.OrderBy(entry => GetSafeCardName(entry.Card), StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Card.index),
            _ => entries.OrderBy(entry => entry.Card.index),
        };

        filteredEntries.AddRange(entries);
    }

    private void EnsureNewCardWillBeVisible()
    {
        searchText = string.Empty;
        filterOption = CardFilterOption.All;
        searchFieldControl?.SetValueWithoutNotify(searchText);
        if (filterFieldControl != null)
        {
            filterFieldControl.index = (int)filterOption;
        }
    }

    private void RefreshCardRowStyles()
    {
        foreach (KeyValuePair<int, Button> pair in cardRowButtons)
        {
            int actualIndex = pair.Key;
            Button row = pair.Value;
            CardData card = database != null && actualIndex >= 0 && actualIndex < database.cards.Count ? database.cards[actualIndex] : null;
            row.text = card != null ? FormatCardListText(card) : string.Empty;
            row.style.backgroundColor = actualIndex == selectedCardIndex
                ? new Color(0.24f, 0.35f, 0.49f)
                : new Color(0.18f, 0.19f, 0.22f);
        }
    }

    private void FocusSelectedCardRow()
    {
        if (cardListScrollView == null)
        {
            return;
        }

        if (cardRowButtons.TryGetValue(selectedCardIndex, out Button row))
        {
            cardListScrollView.schedule.Execute(() =>
            {
                cardListScrollView.ScrollTo(row);
                row.Focus();
            }).ExecuteLater(0);
        }
    }

    private CardData CreateDefaultCard(CardType cardType)
    {
        return new CardData
        {
            index = GetSuggestedId(cardType),
            cardType = cardType,
            name = cardType == CardType.Minion ? "新随从" : "新法术",
            cost = 0,
            attack = cardType == CardType.Minion ? 1 : 0,
            health = cardType == CardType.Minion ? 1 : 0,
            passiveTypes = new List<PassiveType>(),
            aiRole = CardAIRole.None,
            aiPlayStyle = AIPlayStyle.Default,
            aiTargetPriority = AITargetPriority.Default,
            aiBasePriority = 0,
            aiComboReserveThreshold = 0,
            aiLethalBonus = 0,
            effectDescription = string.Empty,
            effects = new List<CardEffectData>(),
            attackAudio = null,
            enterAudio = null,
            image = null,
        };
    }

    private int GetSuggestedId(CardType cardType)
    {
        int start = cardType == CardType.SPELL ? 1101 : 1001;
        int endExclusive = cardType == CardType.SPELL ? 1200 : 1100;
        int max = start - 1;

        foreach (CardData card in database.cards)
        {
            if (card == null || card.cardType != cardType)
            {
                continue;
            }

            if (card.index >= start && card.index < endExclusive && card.index > max)
            {
                max = card.index;
            }
        }

        return max + 1;
    }

    private static CardData CloneCard(CardData source)
    {
        CardData clone = new()
        {
            index = source.index,
            cardType = source.cardType,
            name = source.name,
            cost = source.cost,
            attack = source.attack,
            health = source.health,
            image = source.image,
            effectDescription = source.effectDescription,
            passiveTypes = source.passiveTypes != null ? new List<PassiveType>(source.passiveTypes) : new List<PassiveType>(),
            aiRole = source.aiRole,
            aiPlayStyle = source.aiPlayStyle,
            aiTargetPriority = source.aiTargetPriority,
            aiBasePriority = source.aiBasePriority,
            aiComboReserveThreshold = source.aiComboReserveThreshold,
            aiLethalBonus = source.aiLethalBonus,
            attackAudio = source.attackAudio,
            enterAudio = source.enterAudio,
            effects = new List<CardEffectData>(),
        };

        if (source.effects != null)
        {
            foreach (CardEffectData effect in source.effects)
            {
                clone.effects.Add(CloneEffect(effect));
            }
        }

        return clone;
    }

    private static CardEffectData CloneEffect(CardEffectData source)
    {
        CardEffectData clone = new()
        {
            triggerType = source.triggerType,
            conditionTypes = source.conditionTypes != null ? new List<ConditionType>(source.conditionTypes) : new List<ConditionType>(),
            effectType = source.effectType,
            effectValues = source.effectValues != null ? (int[])source.effectValues.Clone() : Array.Empty<int>(),
            thenEffects = new List<CardEffectData>(),
            elseEffects = new List<CardEffectData>(),
        };

        if (source.thenEffects != null)
        {
            foreach (CardEffectData effect in source.thenEffects)
            {
                clone.thenEffects.Add(CloneEffect(effect));
            }
        }

        if (source.elseEffects != null)
        {
            foreach (CardEffectData effect in source.elseEffects)
            {
                clone.elseEffects.Add(CloneEffect(effect));
            }
        }

        return clone;
    }

    private static string FormatCardListText(CardData card)
    {
        return $"{card.index} | {GetSafeCardName(card)} | {GetCardTypeLabel(card.cardType)} | {card.cost}";
    }

    private static string GetSafeCardName(CardData card)
    {
        return card == null || string.IsNullOrWhiteSpace(card.name) ? "(未命名)" : card.name;
    }

    private static bool IsContiguousSearchMatch(string query, CardData card)
    {
        if (card == null)
        {
            return false;
        }

        string trimmedQuery = query.Trim();
        return card.index.ToString().IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
            || GetSafeCardName(card).IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetCardTypeLabel(CardType cardType)
    {
        return EditorLabelUtility.GetCardTypeLabel(cardType);
    }

    private static string GetSeverityLabel(CardValidationSeverity severity)
    {
        return severity switch
        {
            CardValidationSeverity.Error => "错误",
            CardValidationSeverity.Warning => "警告",
            _ => "提示",
        };
    }

    private static Color GetSeverityColor(CardValidationSeverity severity)
    {
        return severity switch
        {
            CardValidationSeverity.Error => new Color(0.42f, 0.19f, 0.19f),
            CardValidationSeverity.Warning => new Color(0.43f, 0.32f, 0.15f),
            _ => new Color(0.18f, 0.26f, 0.35f),
        };
    }

    private enum CardFilterOption
    {
        All,
        Minion,
        Spell,
    }

    private enum CardSortOption
    {
        IdAscending,
        IdDescending,
        CostAscending,
        CostDescending,
        NameAscending,
    }

    private readonly struct CardListEntry
    {
        public CardListEntry(int actualIndex, CardData card)
        {
            ActualIndex = actualIndex;
            Card = card;
        }

        public int ActualIndex { get; }
        public CardData Card { get; }
    }
}
