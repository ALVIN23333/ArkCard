#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时 GM 调试面板（仅编辑器/开发构建编译）：
/// 设置双方生命/费用、发指定卡牌、填场/清场、直接结束回合、时间倍率、跳过动画、AI 托管。
/// 快捷键：F1 开关面板 / F2 结束回合 / F3 循环倍率(1/2/3) / F4 跳过动画 / F5 AI 托管。
/// </summary>
public class GMCheatPanel : MonoBehaviour
{
    private const KeyCode TogglePanelKey = KeyCode.F1;
    private const KeyCode EndTurnKey = KeyCode.F2;
    private const KeyCode TimescaleKey = KeyCode.F3;
    private const KeyCode InstantAnimKey = KeyCode.F4;
    private const KeyCode AutoPlayKey = KeyCode.F5;

    private static readonly float[] TimescaleOptions = { 0.25f, 0.5f, 1f, 2f, 3f, 5f };
    private static int timescaleIndex = 2;

    private bool show;
    private int targetPlayerIndex;
    private string healthInput = "30";
    private string maxHealthInput = "30";
    private string costInput = "10";
    private string maxCostInput = "10";
    private int selectedCardIndex;
    private string dealCountInput = "1";
    private string cardSearch = string.Empty;
    private Vector2 cardScroll;
    private Vector2 scroll;
    private readonly List<string> logs = new();
    private List<CardData> cardOptions = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindObjectOfType<GMCheatPanel>() != null)
        {
            return;
        }

        GameObject panelObject = new GameObject("GMCheatPanel");
        panelObject.AddComponent<GMCheatPanel>();
        DontDestroyOnLoad(panelObject);
    }

    private void Update()
    {
        if (GUIUtility.keyboardControl != 0)
        {
            return;
        }

        if (Input.GetKeyDown(TogglePanelKey)) show = !show;
        if (Input.GetKeyDown(EndTurnKey)) TryEndTurn();
        if (Input.GetKeyDown(TimescaleKey)) CycleTimescale();
        if (Input.GetKeyDown(InstantAnimKey)) SetInstantAnim(!AnimeManager.Instant);
        if (Input.GetKeyDown(AutoPlayKey)) SetAutoPlay(!IsAutoPlayActive());
    }

    private void OnGUI()
    {
        if (!show)
        {
            return;
        }

        Rect windowRect = new Rect(16f, 16f, 380f, 100f);
        GUILayout.Window(0, windowRect, DrawWindow, "GM 调试面板 (F1 关闭)");
    }

    private void DrawWindow(int windowId)
    {
        GUILayout.BeginVertical();
        DrawPlayerSection();
        DrawStatsSection();
        DrawCardSection();
        DrawFieldSection();
        DrawTurnSection();
        DrawTimescaleSection();
        DrawMiscSection();
        DrawLogSection();
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, Screen.width, 24f));
    }

    private void DrawPlayerSection()
    {
        GUILayout.Label("目标玩家");
        string[] options = { "我方", "敌方" };
        int newIndex = GUILayout.Toolbar(targetPlayerIndex, options);
        if (newIndex != targetPlayerIndex)
        {
            targetPlayerIndex = newIndex;
            PlayerController player = GetTargetPlayer();
            if (player != null)
            {
                healthInput = player.health.ToString();
                maxHealthInput = player.maxHealth.ToString();
                costInput = player.cost.ToString();
                maxCostInput = player.maxCost.ToString();
            }
        }
    }

    private void DrawStatsSection()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("当前生命", GUILayout.Width(60));
        healthInput = GUILayout.TextField(healthInput, GUILayout.Width(48));
        GUILayout.Label("最大生命", GUILayout.Width(60));
        maxHealthInput = GUILayout.TextField(maxHealthInput, GUILayout.Width(48));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("当前费用", GUILayout.Width(60));
        costInput = GUILayout.TextField(costInput, GUILayout.Width(48));
        GUILayout.Label("最大费用", GUILayout.Width(60));
        maxCostInput = GUILayout.TextField(maxCostInput, GUILayout.Width(48));
        GUILayout.EndHorizontal();

        if (GUILayout.Button("应用生命/费用"))
        {
            ApplyStats();
        }
    }

    private void DrawCardSection()
    {
        GUILayout.Label("发指定卡牌到手牌");
        RefreshCardOptions();
        if (cardOptions.Count == 0)
        {
            GUILayout.Label("卡库未加载");
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("查找", GUILayout.Width(40));
        cardSearch = GUILayout.TextField(cardSearch);
        if (GUILayout.Button("清除", GUILayout.Width(48)))
        {
            cardSearch = string.Empty;
        }
        GUILayout.EndHorizontal();

        List<int> visible = GetVisibleCardIndices();
        if (visible.Count == 0)
        {
            GUILayout.Label("没有匹配的卡牌");
        }
        else
        {
            string[] labels = new string[visible.Count];
            for (int i = 0; i < visible.Count; i++)
            {
                CardData card = cardOptions[visible[i]];
                labels[i] = card != null ? $"{card.index} {card.name}" : "?";
            }

            int currentFiltered = visible.IndexOf(selectedCardIndex);
            if (currentFiltered < 0)
            {
                currentFiltered = 0;
            }

            cardScroll = GUILayout.BeginScrollView(cardScroll, GUILayout.Height(150));
            int newFiltered = GUILayout.SelectionGrid(currentFiltered, labels, 2);
            GUILayout.EndScrollView();

            if (newFiltered >= 0 && newFiltered < visible.Count)
            {
                selectedCardIndex = visible[newFiltered];
            }
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("数量", GUILayout.Width(40));
        dealCountInput = GUILayout.TextField(dealCountInput, GUILayout.Width(52));
        if (GUILayout.Button("发牌"))
        {
            DealCards();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawFieldSection()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("填满场地"))
        {
            FillField();
        }
        if (GUILayout.Button("清空场地"))
        {
            ClearField();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawTurnSection()
    {
        if (GUILayout.Button("直接结束当前回合 (F2)"))
        {
            TryEndTurn();
        }
    }

    private void DrawTimescaleSection()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"时间倍率: {Time.timeScale:F2}", GUILayout.Width(120));
        if (GUILayout.Button("暂停(0)"))
        {
            Time.timeScale = 0f;
            Log("已暂停 (timeScale=0)");
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        foreach (float option in TimescaleOptions)
        {
            if (GUILayout.Button(option.ToString("0.##")))
            {
                Time.timeScale = option;
                timescaleIndex = System.Array.IndexOf(TimescaleOptions, option);
                Log($"时间倍率 -> {option}");
            }
        }
        GUILayout.EndHorizontal();
    }

    private void DrawMiscSection()
    {
        bool newInstant = GUILayout.Toggle(AnimeManager.Instant, "跳过动画（瞬时完成）(F4)");
        if (newInstant != AnimeManager.Instant)
        {
            SetInstantAnim(newInstant);
        }

        bool newAutoPlay = GUILayout.Toggle(IsAutoPlayActive(), "主玩家 AI 托管 (F5)");
        if (newAutoPlay != IsAutoPlayActive())
        {
            SetAutoPlay(newAutoPlay);
        }
    }

    private void DrawLogSection()
    {
        GUILayout.Label("日志");
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(120));
        foreach (string entry in logs)
        {
            GUILayout.Label(entry);
        }
        GUILayout.EndScrollView();
    }

    private void ApplyStats()
    {
        PlayerController player = GetTargetPlayer();
        if (player == null)
        {
            Log("目标玩家不存在");
            return;
        }

        player.SetHealth(ParseInt(healthInput, player.health));
        player.SetMaxHealth(ParseInt(maxHealthInput, player.maxHealth));
        player.SetCost(ParseInt(costInput, player.cost));
        player.SetMaxCost(ParseInt(maxCostInput, player.maxCost));
        Log($"已设置 {player.name}: HP={player.health}/{player.maxHealth}, 费用={player.cost}/{player.maxCost}");
    }

    private List<int> GetVisibleCardIndices()
    {
        List<int> visible = new();
        string query = cardSearch.Trim().ToLowerInvariant();
        for (int i = 0; i < cardOptions.Count; i++)
        {
            CardData card = cardOptions[i];
            if (card == null)
            {
                continue;
            }

            bool matches = string.IsNullOrEmpty(query)
                || (card.name != null && card.name.ToLowerInvariant().Contains(query))
                || card.index.ToString().Contains(query);
            if (matches)
            {
                visible.Add(i);
            }
        }
        return visible;
    }

    private void DealCards()
    {
        PlayerController player = GetTargetPlayer();
        RefreshCardOptions();
        if (player == null)
        {
            Log("目标玩家不存在");
            return;
        }
        if (cardOptions.Count == 0)
        {
            Log("卡库未加载");
            return;
        }
        if (selectedCardIndex < 0 || selectedCardIndex >= cardOptions.Count || cardOptions[selectedCardIndex] == null)
        {
            Log("未选择有效卡牌");
            return;
        }
        if (GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.cardPrefab == null)
        {
            Log("cardPrefab 未配置");
            return;
        }
        if (player.handController == null)
        {
            Log($"{player.name} 没有 HandController");
            return;
        }

        CardData data = cardOptions[selectedCardIndex];
        int count = Mathf.Clamp(ParseInt(dealCountInput, 1), 1, 20);
        for (int i = 0; i < count; i++)
        {
            CardController card = Instantiate(GM.Ins.BM.cardPrefab).GetComponent<CardController>();
            card.Init(data, player);
            if (card.cardDisplay != null)
            {
                card.cardDisplay.ShowBack(!player.isMainPlayer);
            }
            player.handController.AddCard(card);
        }
        Log($"给 {player.name} 发了 {count} 张 {data.name}");
    }

    private void FillField()
    {
        PlayerController player = GetTargetPlayer();
        if (player == null || player.fieldController == null)
        {
            Log("目标玩家或场地不存在");
            return;
        }
        if (GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.cardPrefab == null)
        {
            Log("cardPrefab 未配置");
            return;
        }

        RefreshCardOptions();
        List<CardData> minions = new();
        foreach (CardData card in cardOptions)
        {
            if (card != null && card.cardType == CardType.Minion)
            {
                minions.Add(card);
            }
        }
        if (minions.Count == 0)
        {
            Log("卡库中没有随从卡");
            return;
        }

        int added = 0;
        while (player.fieldController.fieldCards.Count < GameConst.fieldMax)
        {
            CardData data = minions[Random.Range(0, minions.Count)];
            CardController card = Instantiate(GM.Ins.BM.cardPrefab).GetComponent<CardController>();
            card.Init(data, player);
            if (card.cardDisplay != null)
            {
                card.cardDisplay.ShowBack(false);
            }
            player.fieldController.AddCard(card);
            added++;
        }
        Log($"已填满 {player.name} 的场地（新增 {added} 张）");
    }

    private void ClearField()
    {
        PlayerController player = GetTargetPlayer();
        if (player == null || player.fieldController == null)
        {
            Log("目标玩家或场地不存在");
            return;
        }

        List<CardController> cards = new(player.fieldController.fieldCards);
        foreach (CardController card in cards)
        {
            if (card != null)
            {
                player.SendCardToGraveyard(card);
            }
        }
        Log($"已清空 {player.name} 的场地");
    }

    private void TryEndTurn()
    {
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            Log("BM 不存在");
            return;
        }

        BattleManager battleManager = GM.Ins.BM;
        if (battleManager.IsGameOver)
        {
            Log("游戏已结束，无法结束回合");
            return;
        }
        if (battleManager.IsTurnTransitioning)
        {
            Log("回合转换中，无法结束回合");
            return;
        }
        if (battleManager.EM != null && battleManager.EM.IsProcessingEffects)
        {
            Log("效果结算中，无法结束回合");
            return;
        }

        PlayerController current = battleManager.CurrentPlayer;
        if (current == null || !current.isInTurn)
        {
            Log("当前玩家不在回合中");
            return;
        }

        if (current.isMainPlayer)
        {
            battleManager.EndMainPlayerTurn();
        }
        else
        {
            battleManager.EndCurrentTurn();
        }
        Log("已请求结束回合");
    }

    private void CycleTimescale()
    {
        timescaleIndex = timescaleIndex >= 4 ? 2 : timescaleIndex + 1; // 1 -> 2 -> 3 -> 1
        Time.timeScale = TimescaleOptions[timescaleIndex];
        Log($"时间倍率 -> {Time.timeScale}");
    }

    private void SetInstantAnim(bool value)
    {
        AnimeManager.Instant = value;
        Log(value ? "已开启跳过动画" : "已关闭跳过动画");
    }

    private void SetAutoPlay(bool value)
    {
        AutoPlayDriver driver = AutoPlayDriver.GetOrCreate();
        driver.enabled = value;
        Log(value ? "主玩家 AI 托管：开" : "主玩家 AI 托管：关");
    }

    private bool IsAutoPlayActive()
    {
        AutoPlayDriver driver = FindObjectOfType<AutoPlayDriver>();
        return driver != null && driver.enabled;
    }

    private PlayerController GetTargetPlayer()
    {
        if (GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.players == null)
        {
            return null;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player != null && player.isMainPlayer == (targetPlayerIndex == 0))
            {
                return player;
            }
        }
        return null;
    }

    private void RefreshCardOptions()
    {
        if (GM.Ins == null || GM.Ins.DM == null || GM.Ins.DM.so == null || GM.Ins.DM.so.cards == null)
        {
            return;
        }
        if (cardOptions.Count == GM.Ins.DM.so.cards.Count)
        {
            return;
        }
        cardOptions = GM.Ins.DM.so.cards;
    }

    private void Log(string message)
    {
        logs.Add($"[{Time.realtimeSinceStartup:F1}s] {message}");
        if (logs.Count > 30)
        {
            logs.RemoveAt(0);
        }
    }

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text, out int value) ? value : fallback;
    }
}
#endif
