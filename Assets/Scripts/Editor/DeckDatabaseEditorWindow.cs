using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class DeckDatabaseEditorWindow : EditorWindow
{
    private const string DefaultDeckDatabasePath = "Assets/Resources/DeckListDatabase.asset";
    private const string DefaultCardDatabasePath = "Assets/Resources/ArkCardsDatabase.asset";

    private readonly Dictionary<int, Button> deckRowButtons = new();
    private readonly Dictionary<int, Button> cardRowButtons = new();
    private readonly Dictionary<int, Button> cardAddButtons = new();

    private DeckListSO deckDatabase;
    private CardListSO cardDatabase;
    private int selectedDeckIndex = -1;
    private int selectedCardIndex = -1;
    private string cardSearchText = string.Empty;
    private CardFilterOption cardFilterOption = CardFilterOption.All;
    private CardSortOption cardSortOption = CardSortOption.IdAscending;

    private ObjectField deckDatabaseField;
    private ObjectField cardDatabaseField;
    private PopupField<string> playerDeckPopup;
    private PopupField<string> aiDeckPopup;
    private Button saveButton;
    private Button refreshButton;
    private bool suppressPopupCallbacks;

    private VisualElement leftPanel;
    private ScrollView deckListScrollView;
    private TextField deckNameField;
    private Label deckCountLabel;
    private Button newDeckButton;
    private Button deleteDeckButton;

    private VisualElement middlePanel;
    private ScrollView cardListScrollView;
    private TextField cardSearchField;
    private PopupField<string> cardFilterField;
    private PopupField<string> cardSortField;

    private VisualElement rightPanel;
    private ScrollView deckCardScrollView;

    [MenuItem("Tools/ArkCards/Deck Editor")]
    public static void Open()
    {
        DeckDatabaseEditorWindow window = GetWindow<DeckDatabaseEditorWindow>();
        window.titleContent = new GUIContent("Deck Editor");
        window.minSize = new Vector2(1200f, 700f);
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("Deck Editor");
        Undo.undoRedoPerformed += HandleUndoRedo;
        TryLoadDefaultDatabases();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
    }

    private void OnFocus()
    {
        if (deckListScrollView != null)
        {
            RefreshAllPanels();
        }
    }

    private void CreateGUI()
    {
        BuildWindowShell();
        RefreshAllPanels();
    }

    private void HandleUndoRedo()
    {
        RefreshAllPanels();
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

        TwoPaneSplitView outerSplit = new(0, 300f, TwoPaneSplitViewOrientation.Horizontal);
        outerSplit.style.flexGrow = 1;
        rootVisualElement.Add(outerSplit);

        leftPanel = CreatePanelContainer();
        leftPanel.style.width = 300;
        outerSplit.Add(leftPanel);

        float rightPanelWidth = Mathf.Max(360f, (position.width - 316f) / 3f);
        TwoPaneSplitView rightSplit = new(1, rightPanelWidth, TwoPaneSplitViewOrientation.Horizontal);
        rightSplit.style.flexGrow = 1;
        outerSplit.Add(rightSplit);

        middlePanel = new VisualElement();
        middlePanel.style.flexGrow = 1;
        rightSplit.Add(middlePanel);
        BuildMiddleLibraryControls();

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

        Label title = new("卡组编辑器");
        title.style.minWidth = 90;
        title.style.marginRight = 8;
        header.Add(title);

        deckDatabaseField = new ObjectField
        {
            objectType = typeof(DeckListSO),
            allowSceneObjects = false,
        };
        deckDatabaseField.style.minWidth = 180;
        deckDatabaseField.style.flexGrow = 1;
        deckDatabaseField.style.marginRight = 8;
        deckDatabaseField.RegisterValueChangedCallback(evt =>
        {
            deckDatabase = evt.newValue as DeckListSO;
            selectedDeckIndex = deckDatabase != null && deckDatabase.decks != null && deckDatabase.decks.Count > 0 ? 0 : -1;
            RefreshAllPanels();
        });
        header.Add(deckDatabaseField);

        cardDatabaseField = new ObjectField
        {
            objectType = typeof(CardListSO),
            allowSceneObjects = false,
        };
        cardDatabaseField.style.minWidth = 180;
        cardDatabaseField.style.flexGrow = 1;
        cardDatabaseField.style.marginRight = 8;
        cardDatabaseField.RegisterValueChangedCallback(evt =>
        {
            cardDatabase = evt.newValue as CardListSO;
            selectedCardIndex = -1;
            RefreshAllPanels();
        });
        header.Add(cardDatabaseField);

        refreshButton = CreateActionButton("刷新", RefreshAllPanels);
        header.Add(refreshButton);

        saveButton = CreateActionButton("保存", SaveDatabase);
        header.Add(saveButton);

        return header;
    }

    private void BuildLeftPanel()
    {
        leftPanel.Clear();
        deckRowButtons.Clear();

        VisualElement deckListArea = new();
        deckListArea.style.flexGrow = 3;
        deckListArea.style.minHeight = 0;
        deckListArea.style.height = new Length(75f, LengthUnit.Percent);
        leftPanel.Add(deckListArea);

        deckListArea.Add(BuildSectionTitle("卡组列表"));

        VisualElement buttonRow = CreateButtonRow();
        newDeckButton = CreateActionButton("新建卡组", CreateNewDeck);
        deleteDeckButton = CreateActionButton("删除卡组", DeleteSelectedDeck);
        buttonRow.Add(newDeckButton);
        buttonRow.Add(deleteDeckButton);
        deckListArea.Add(buttonRow);

        deckNameField = new TextField("卡组名称");
        deckNameField.isDelayed = true;
        deckNameField.RegisterValueChangedCallback(evt =>
        {
            if (!HasDeckSelection())
            {
                return;
            }
            ApplyObjectChange("Rename Deck", () => GetSelectedDeck().name = evt.newValue ?? string.Empty, refreshMiddle: false, refreshRight: false);
        });
        deckListArea.Add(deckNameField);

        deckCountLabel = new Label();
        deckCountLabel.style.marginTop = 4;
        deckCountLabel.style.marginBottom = 4;
        deckListArea.Add(deckCountLabel);

        deckListScrollView = new ScrollView();
        deckListScrollView.style.flexGrow = 1;
        deckListScrollView.style.marginTop = 4;
        deckListArea.Add(deckListScrollView);

        VisualElement assignmentArea = new();
        assignmentArea.style.flexGrow = 1;
        assignmentArea.style.minHeight = 0;
        assignmentArea.style.height = new Length(25f, LengthUnit.Percent);
        assignmentArea.style.marginTop = 8;
        assignmentArea.style.paddingTop = 8;
        assignmentArea.style.borderTopWidth = 1;
        assignmentArea.style.borderTopColor = new Color(0.28f, 0.3f, 0.32f);
        leftPanel.Add(assignmentArea);

        assignmentArea.Add(BuildSectionTitle("对战卡组配置"));

        playerDeckPopup = new PopupField<string>("玩家卡组", GetDeckChoices(), 0);
        playerDeckPopup.style.minWidth = 120;
        playerDeckPopup.RegisterValueChangedCallback(evt =>
        {
            if (suppressPopupCallbacks || deckDatabase == null)
            {
                return;
            }
            ApplyObjectChange("Set Player Deck", () => deckDatabase.playerDeckIndex = playerDeckPopup.index - 1, refreshMiddle: false, refreshRight: false);
        });
        assignmentArea.Add(playerDeckPopup);

        aiDeckPopup = new PopupField<string>("AI 卡组", GetDeckChoices(), 0);
        aiDeckPopup.style.minWidth = 120;
        aiDeckPopup.RegisterValueChangedCallback(evt =>
        {
            if (suppressPopupCallbacks || deckDatabase == null)
            {
                return;
            }
            ApplyObjectChange("Set AI Deck", () => deckDatabase.aiDeckIndex = aiDeckPopup.index - 1, refreshMiddle: false, refreshRight: false);
        });
        assignmentArea.Add(aiDeckPopup);
    }

    private void RefreshAllPanels()
    {
        TryLoadDefaultDatabases();
        ClampSelections();
        UpdateHeaderState();
        UpdateToolbarState();
        RefreshDeckList();
        RefreshMiddlePanel();
        RefreshRightPanel();
    }

    private void RefreshDeckList()
    {
        if (deckListScrollView == null)
        {
            return;
        }

        Vector2 previousScroll = deckListScrollView.scrollOffset;
        deckListScrollView.Clear();
        deckRowButtons.Clear();

        if (deckDatabase == null || deckDatabase.decks == null)
        {
            deckListScrollView.Add(new HelpBox("未找到卡组数据库，请在上方选择一个 DeckListSO 资产。", HelpBoxMessageType.Warning));
        }
        else if (deckDatabase.decks.Count == 0)
        {
            deckListScrollView.Add(new Label("暂无卡组，点击“新建卡组”。"));
        }
        else
        {
            for (int i = 0; i < deckDatabase.decks.Count; i++)
            {
                int index = i;
                DeckData deck = deckDatabase.decks[i];
                Button row = new Button(() => SelectDeck(index))
                {
                    text = $"{index + 1}. {GetSafeDeckName(deck)}（{GetDeckCardCount(deck)}/{GameConst.librarymax}）",
                };
                row.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.style.marginBottom = 2;
                deckRowButtons[index] = row;
                deckListScrollView.Add(row);
            }
        }

        RefreshDeckRowStyles();
        deckListScrollView.schedule.Execute(() => deckListScrollView.scrollOffset = previousScroll).ExecuteLater(0);

        bool hasSelection = HasDeckSelection();
        deckNameField?.SetEnabled(hasSelection);
        deckNameField?.SetValueWithoutNotify(hasSelection ? GetSelectedDeck().name : string.Empty);
        deckCountLabel.text = hasSelection
            ? $"当前卡组：{GetSafeDeckName(GetSelectedDeck())}（{GetDeckCardCount(GetSelectedDeck())}/{GameConst.librarymax}）"
            : "未选择卡组";
    }

    private void RefreshMiddlePanel()
    {
        if (middlePanel == null)
        {
            return;
        }

        if (cardListScrollView == null)
        {
            BuildMiddleLibraryControls();
        }

        cardRowButtons.Clear();
        cardAddButtons.Clear();
        cardListScrollView.Clear();

        if (cardDatabase == null || cardDatabase.cards == null)
        {
            cardListScrollView.Add(new HelpBox("请先选择卡牌数据库（默认 ArkCardsDatabase）。", HelpBoxMessageType.Info));
            return;
        }

        List<(int Index, CardData Card)> visibleEntries = BuildVisibleCardEntries();
        if (visibleEntries.Count == 0)
        {
            cardListScrollView.Add(new Label("没有匹配的卡牌。"));
            return;
        }

        bool canAdd = HasDeckSelection() && GetDeckCardCount(GetSelectedDeck()) < GameConst.librarymax;
        foreach ((int index, CardData card) in visibleEntries)
        {
            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;

            Button selectButton = new Button(() => SelectCard(index))
            {
                text = FormatCardRow(card),
            };
            selectButton.style.flexGrow = 1;
            selectButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            cardRowButtons[index] = selectButton;
            row.Add(selectButton);

            Button addButton = new Button(() => AddCardToDeck(card.index)) { text = "添加" };
            addButton.style.marginLeft = 8;
            addButton.SetEnabled(canAdd);
            cardAddButtons[index] = addButton;
            row.Add(addButton);

            cardListScrollView.Add(row);
        }

        RefreshCardRowStyles();
    }

    private void BuildMiddleLibraryControls()
    {
        middlePanel.Clear();

        middlePanel.Add(BuildSectionTitle("卡牌库"));

        cardSearchField = new TextField("搜索") { value = cardSearchText };
        cardSearchField.RegisterValueChangedCallback(evt =>
        {
            cardSearchText = evt.newValue ?? string.Empty;
            RefreshMiddlePanel();
        });
        middlePanel.Add(cardSearchField);

        cardFilterField = new PopupField<string>("类型筛选", new List<string> { "全部", "随从", "法术" }, (int)cardFilterOption);
        cardFilterField.RegisterValueChangedCallback(evt =>
        {
            cardFilterOption = evt.newValue switch
            {
                "随从" => CardFilterOption.Minion,
                "法术" => CardFilterOption.Spell,
                _ => CardFilterOption.All,
            };
            RefreshMiddlePanel();
        });
        middlePanel.Add(cardFilterField);

        cardSortField = new PopupField<string>("列表排序", new List<string>
        {
            "ID 升序",
            "ID 降序",
            "费用 升序",
            "费用 降序",
            "名称 A-Z",
        }, (int)cardSortOption);
        cardSortField.RegisterValueChangedCallback(evt =>
        {
            cardSortOption = evt.newValue switch
            {
                "ID 降序" => CardSortOption.IdDescending,
                "费用 升序" => CardSortOption.CostAscending,
                "费用 降序" => CardSortOption.CostDescending,
                "名称 A-Z" => CardSortOption.NameAscending,
                _ => CardSortOption.IdAscending,
            };
            RefreshMiddlePanel();
        });
        middlePanel.Add(cardSortField);

        cardListScrollView = new ScrollView();
        cardListScrollView.style.flexGrow = 1;
        cardListScrollView.style.marginLeft = 8;
        cardListScrollView.style.marginRight = 8;
        cardListScrollView.style.paddingLeft = 8;
        cardListScrollView.style.paddingRight = 8;
        cardListScrollView.style.paddingTop = 8;
        middlePanel.Add(cardListScrollView);
    }

    private List<(int Index, CardData Card)> BuildVisibleCardEntries()
    {
        List<(int Index, CardData Card)> entries = new();
        for (int i = 0; i < cardDatabase.cards.Count; i++)
        {
            entries.Add((i, cardDatabase.cards[i]));
        }

        if (!string.IsNullOrWhiteSpace(cardSearchText))
        {
            entries = entries.Where(entry => IsContiguousCardSearchMatch(cardSearchText, entry.Card)).ToList();
        }

        entries = cardFilterOption switch
        {
            CardFilterOption.Minion => entries.Where(entry => entry.Card.cardType == CardType.Minion).ToList(),
            CardFilterOption.Spell => entries.Where(entry => entry.Card.cardType == CardType.SPELL).ToList(),
            _ => entries,
        };

        entries = cardSortOption switch
        {
            CardSortOption.IdDescending => entries.OrderByDescending(entry => entry.Card.index).ToList(),
            CardSortOption.CostAscending => entries.OrderBy(entry => entry.Card.cost).ThenBy(entry => entry.Card.index).ToList(),
            CardSortOption.CostDescending => entries.OrderByDescending(entry => entry.Card.cost).ThenBy(entry => entry.Card.index).ToList(),
            CardSortOption.NameAscending => entries.OrderBy(entry => GetSafeCardName(entry.Card), StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Card.index).ToList(),
            _ => entries.OrderBy(entry => entry.Card.index).ToList(),
        };

        return entries;
    }

    private void RefreshCardAddButtonStates()
    {
        bool canAdd = HasDeckSelection() && GetDeckCardCount(GetSelectedDeck()) < GameConst.librarymax;
        foreach (Button addButton in cardAddButtons.Values)
        {
            addButton.SetEnabled(canAdd);
        }
    }

    private void RefreshRightPanel()
    {
        if (rightPanel == null)
        {
            return;
        }

        rightPanel.Clear();
        rightPanel.Add(BuildSectionTitle("当前卡组"));

        if (deckDatabase == null)
        {
            rightPanel.Add(new HelpBox("请先选择卡组数据库。", HelpBoxMessageType.Info));
            return;
        }

        if (!HasDeckSelection())
        {
            rightPanel.Add(new HelpBox("请先选择或新建一个卡组。", HelpBoxMessageType.Info));
            return;
        }

        DeckData deck = GetSelectedDeck();
        deckCardScrollView = new ScrollView();
        deckCardScrollView.style.flexGrow = 1;
        deckCardScrollView.style.paddingLeft = 8;
        deckCardScrollView.style.paddingRight = 8;
        deckCardScrollView.style.paddingTop = 8;
        rightPanel.Add(deckCardScrollView);

        if (deck.deck == null || deck.deck.Count == 0)
        {
            deckCardScrollView.Add(new Label("卡组为空。"));
            return;
        }

        Dictionary<int, int> counts = BuildDeckCounts(deck);
        foreach (KeyValuePair<int, int> pair in counts)
        {
            int cardId = pair.Key;
            CardData card = cardDatabase != null ? cardDatabase.GetData(cardId) : null;

            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;

            Label label = new(card != null ? $"{cardId} | {GetSafeCardName(card)}" : $"{cardId} | （已缺失）");
            label.style.flexGrow = 1;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(label);

            Label countLabel = new($"x{pair.Value}");
            countLabel.style.minWidth = 28;
            countLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(countLabel);

            Button removeButton = new Button(() => RemoveCardFromDeck(cardId)) { text = "移除" };
            removeButton.style.marginLeft = 8;
            row.Add(removeButton);
            deckCardScrollView.Add(row);
        }
    }

    private void SelectDeck(int index)
    {
        if (deckDatabase == null || deckDatabase.decks == null || index < 0 || index >= deckDatabase.decks.Count)
        {
            return;
        }

        selectedDeckIndex = index;
        RefreshDeckList();
        RefreshRightPanel();
    }

    private void SelectCard(int index)
    {
        if (cardDatabase == null || cardDatabase.cards == null || index < 0 || index >= cardDatabase.cards.Count)
        {
            return;
        }

        selectedCardIndex = index;
        RefreshCardRowStyles();
        RefreshRightPanel();
    }

    private void CreateNewDeck()
    {
        if (deckDatabase == null)
        {
            return;
        }

        ApplyObjectChange("New Deck", () =>
        {
            DeckData deck = new DeckData { name = "新卡组" };
            deckDatabase.decks.Add(deck);
            selectedDeckIndex = deckDatabase.decks.Count - 1;
        }, refreshMiddle: false);
    }

    private void DeleteSelectedDeck()
    {
        if (!HasDeckSelection())
        {
            return;
        }

        DeckData deck = GetSelectedDeck();
        if (!EditorUtility.DisplayDialog(
                "删除卡组",
                $"确认删除卡组“{GetSafeDeckName(deck)}”吗？",
                "删除",
                "取消"))
        {
            return;
        }

        ApplyObjectChange("Delete Deck", () =>
        {
            int removedIndex = selectedDeckIndex;
            deckDatabase.decks.RemoveAt(removedIndex);
            RemapAssignmentIndex(ref deckDatabase.playerDeckIndex, removedIndex);
            RemapAssignmentIndex(ref deckDatabase.aiDeckIndex, removedIndex);
            selectedDeckIndex = deckDatabase.decks.Count == 0 ? -1 : Mathf.Clamp(removedIndex, 0, deckDatabase.decks.Count - 1);
        }, refreshMiddle: false);
    }

    private void AddCardToDeck(int cardId)
    {
        if (!HasDeckSelection())
        {
            ShowNotification(new GUIContent("请先选择或新建一个卡组"));
            return;
        }

        DeckData deck = GetSelectedDeck();
        if (deck.deck == null || deck.deck.Count >= GameConst.librarymax)
        {
            ShowNotification(new GUIContent($"卡组已满（{GameConst.librarymax} 张）"));
            return;
        }

        ApplyObjectChange("Add Card to Deck", () => GetSelectedDeck().deck.Add(cardId), refreshMiddle: false);
    }

    private void RemoveCardFromDeck(int cardId)
    {
        if (!HasDeckSelection())
        {
            return;
        }

        DeckData deck = GetSelectedDeck();
        if (deck.deck == null || deck.deck.Count == 0)
        {
            return;
        }

        ApplyObjectChange("Remove Card from Deck", () => deck.deck.Remove(cardId), refreshMiddle: false);
    }

    private void ApplyObjectChange(string undoName, Action change, bool refreshMiddle = true, bool refreshRight = true)
    {
        if (deckDatabase == null)
        {
            return;
        }

        Undo.RecordObject(deckDatabase, undoName);
        change?.Invoke();
        EditorUtility.SetDirty(deckDatabase);
        ClampSelections();
        UpdateHeaderState();
        UpdateToolbarState();
        RefreshDeckList();
        if (refreshMiddle)
        {
            RefreshMiddlePanel();
        }
        else
        {
            RefreshCardAddButtonStates();
        }

        if (refreshRight)
        {
            RefreshRightPanel();
        }
    }

    private void SaveDatabase()
    {
        if (deckDatabase == null)
        {
            return;
        }

        EditorUtility.SetDirty(deckDatabase);
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent("卡组数据库已保存"));
    }

    private void RefreshDeckRowStyles()
    {
        foreach (KeyValuePair<int, Button> pair in deckRowButtons)
        {
            pair.Value.style.backgroundColor = pair.Key == selectedDeckIndex
                ? new Color(0.24f, 0.35f, 0.49f)
                : new Color(0.18f, 0.19f, 0.22f);
        }
    }

    private void RefreshCardRowStyles()
    {
        foreach (KeyValuePair<int, Button> pair in cardRowButtons)
        {
            pair.Value.style.backgroundColor = pair.Key == selectedCardIndex
                ? new Color(0.24f, 0.35f, 0.49f)
                : new Color(0.18f, 0.19f, 0.22f);
        }
    }

    private void UpdateHeaderState()
    {
        deckDatabaseField?.SetValueWithoutNotify(deckDatabase);
        cardDatabaseField?.SetValueWithoutNotify(cardDatabase);

        suppressPopupCallbacks = true;
        try
        {
            if (playerDeckPopup != null)
            {
                playerDeckPopup.SetEnabled(deckDatabase != null);
                playerDeckPopup.choices = GetDeckChoices();
                playerDeckPopup.index = deckDatabase != null ? deckDatabase.playerDeckIndex + 1 : 0;
            }

            if (aiDeckPopup != null)
            {
                aiDeckPopup.SetEnabled(deckDatabase != null);
                aiDeckPopup.choices = GetDeckChoices();
                aiDeckPopup.index = deckDatabase != null ? deckDatabase.aiDeckIndex + 1 : 0;
            }
        }
        finally
        {
            suppressPopupCallbacks = false;
        }
    }

    private void UpdateToolbarState()
    {
        bool hasDeckDatabase = deckDatabase != null;
        newDeckButton?.SetEnabled(hasDeckDatabase);
        deleteDeckButton?.SetEnabled(hasDeckDatabase && HasDeckSelection());
        saveButton?.SetEnabled(hasDeckDatabase);
        refreshButton?.SetEnabled(true);
    }

    private void TryLoadDefaultDatabases()
    {
        if (deckDatabase == null)
        {
            deckDatabase = AssetDatabase.LoadAssetAtPath<DeckListSO>(DefaultDeckDatabasePath);
        }

        if (cardDatabase == null)
        {
            cardDatabase = AssetDatabase.LoadAssetAtPath<CardListSO>(DefaultCardDatabasePath);
        }
    }

    private void ClampSelections()
    {
        if (deckDatabase == null || deckDatabase.decks == null || deckDatabase.decks.Count == 0)
        {
            selectedDeckIndex = -1;
        }
        else if (selectedDeckIndex < 0 || selectedDeckIndex >= deckDatabase.decks.Count)
        {
            selectedDeckIndex = Mathf.Clamp(selectedDeckIndex, 0, deckDatabase.decks.Count - 1);
        }

        if (cardDatabase == null || cardDatabase.cards == null || cardDatabase.cards.Count == 0)
        {
            selectedCardIndex = -1;
        }
        else if (selectedCardIndex < 0 || selectedCardIndex >= cardDatabase.cards.Count)
        {
            selectedCardIndex = Mathf.Clamp(selectedCardIndex, 0, cardDatabase.cards.Count - 1);
        }
    }

    private bool HasDeckSelection()
    {
        return deckDatabase != null
            && deckDatabase.decks != null
            && selectedDeckIndex >= 0
            && selectedDeckIndex < deckDatabase.decks.Count;
    }

    private DeckData GetSelectedDeck()
    {
        return HasDeckSelection() ? deckDatabase.decks[selectedDeckIndex] : null;
    }

    private List<string> GetDeckChoices()
    {
        List<string> choices = new() { "(未选择)" };
        if (deckDatabase == null || deckDatabase.decks == null)
        {
            return choices;
        }

        for (int i = 0; i < deckDatabase.decks.Count; i++)
        {
            choices.Add(GetSafeDeckName(deckDatabase.decks[i]));
        }

        return choices;
    }

    private static void RemapAssignmentIndex(ref int index, int removedIndex)
    {
        if (index == removedIndex)
        {
            index = -1;
        }
        else if (index > removedIndex)
        {
            index--;
        }
    }

    private static int GetDeckCardCount(DeckData deck)
    {
        return deck != null && deck.deck != null ? deck.deck.Count : 0;
    }

    private static Dictionary<int, int> BuildDeckCounts(DeckData deck)
    {
        Dictionary<int, int> counts = new();
        if (deck == null || deck.deck == null)
        {
            return counts;
        }

        foreach (int cardId in deck.deck)
        {
            counts.TryGetValue(cardId, out int current);
            counts[cardId] = current + 1;
        }

        return counts;
    }

    private static string GetSafeDeckName(DeckData deck)
    {
        return deck == null || string.IsNullOrWhiteSpace(deck.name) ? "(未命名)" : deck.name;
    }

    private static string GetSafeCardName(CardData card)
    {
        return card == null || string.IsNullOrWhiteSpace(card.name) ? "(未命名)" : card.name;
    }

    private static bool IsContiguousCardSearchMatch(string query, CardData card)
    {
        if (card == null)
        {
            return false;
        }

        string trimmedQuery = query.Trim();
        return card.index.ToString().IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
            || GetSafeCardName(card).IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string FormatCardRow(CardData card)
    {
        return card == null
            ? "（空卡位）"
            : $"{card.index} | {GetSafeCardName(card)} | {EditorLabelUtility.GetCardTypeLabel(card.cardType)} | {card.cost}";
    }

    private static VisualElement CreatePanelContainer()
    {
        VisualElement container = new();
        container.style.flexGrow = 1;
        container.style.paddingBottom = 8;
        container.style.paddingLeft = 8;
        container.style.paddingRight = 8;
        container.style.paddingTop = 8;
        return container;
    }

    private static VisualElement BuildSectionTitle(string text)
    {
        Label label = new(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.fontSize = 15;
        label.style.marginBottom = 6;
        return label;
    }

    private static VisualElement CreateButtonRow()
    {
        VisualElement row = new();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 4;
        return row;
    }

    private static Button CreateActionButton(string text, Action onClick)
    {
        Button button = new(onClick) { text = text };
        button.style.flexGrow = 1;
        button.style.marginRight = 4;
        return button;
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
}
