// ============================================================================
// 【改动 2（配套）/ LLM 提示词与 confidence 缺省值】
//
// 本文件 = Assets/Scripts/AI/LLM/LLMStrategyClient.cs 的修改副本。
// 原文件未做任何改动，本副本只放在 new/ 目录下供你审阅后手工搬回。
//
// 与原文件的差异（共 2 处）：
//   [1] LLMStrategyHint.confidence 默认值 0f -> 1f。
//       原因：AIController 现在会做 confidence >= 0.6 的阈值判断，
//       如果模型没有返回 confidence 字段，JsonUtility 会填 0，
//       导致所有建议都被当成“低置信度”丢弃。缺省为 1f 表示
//       “未提供置信度时按可信处理”，同时保留模型显式返回低分时被拒绝的能力。
//   [2] System Prompt 改写为文档 3.5 节的结构：
//       角色 + 游戏规则 + 只返回 JSON 的格式约束 + 只从 legal_actions 里选。
//       （原提示词只说了“只能选一个 action_index”，缺少游戏规则说明）
//
// 接口契约保持不变：输入 LLMStrategyPrompt / 输出 LLMStrategyHint，
// 依旧只允许 LLM 返回合法动作下标，越界即丢弃。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class LLMMessage
{
    public string role;
    public string content;
}

[Serializable]
public sealed class LLMChatRequest
{
    public string model;
    public LLMMessage[] messages;
    public float temperature;

    // 【改动 2 配套 / 修复 HTTP 400】
    // 原代码这里有一个 response_format = { type = "json_object" } 字段。
    // 智谱接口对不支持的模型会直接返回：
    //   HTTP 400 {"error":{"message":"当前模型不支持指定的 response_format，请调整参数后重试。",
    //             "type":"invalid_request_error","code":"invalid_request_error"}}
    // 官方给出的解决方案就是「移除 response_format 参数」或改用支持的模型。
    // 既然 System Prompt 已经明确要求"只返回 JSON"，这里直接去掉该字段；
    // 解析端 ParseHint 本来就有 try/catch 降级，去掉后不会更不安全。
    //
    // 如果以后要用支持结构化输出的模型，可以把下面这行取消注释：
    // public LLMResponseFormat response_format = new();
}

/// <summary>保留类型定义，供需要 response_format 时使用（当前默认不发送）。</summary>
[Serializable]
public sealed class LLMResponseFormat
{
    public string type = "json_object";
}

[Serializable]
public sealed class LLMChatChoice
{
    public LLMMessage message;
}

[Serializable]
public sealed class LLMChatResponse
{
    public LLMChatChoice[] choices;
}

[Serializable]
public sealed class LLMStrategyPrompt
{
    public int current_player_health;
    public int opponent_health;
    public int current_player_cost;
    public int current_player_field_count;
    public int opponent_field_count;
    public LLMActionOption[] legal_actions;
}

[Serializable]
public sealed class LLMStrategyHint
{
    public int action_index = -1;
    public string strategy = string.Empty;
    // [1] 缺省 1f：未返回 confidence 时按“可信”处理，避免被阈值一律拒绝
    public float confidence = 1f;
}

public sealed class LLMStrategyClient : MonoBehaviour
{
    [SerializeField] private string endpoint = "https://open.bigmodel.cn/api/paas/v4/chat/completions";
    // 【改动 2 配套 / 换模型】原为 glm-4-flash，实测它有两个问题：
    //   1) 返回的 content 会被 Markdown 代码块包起来（```json ... ```），
    //      需要 StripCodeFence 兜底才能解析；
    //   2) strategy 返回英文文本，放进中文文档不整齐。
    // glm-4-plus 返回的是干净 JSON，且 strategy 为中文。
    // 注意：本组件在场景里没有序列化实例（运行时由 ResolveLLMClient 自动 AddComponent），
    // 所以这里改默认值即可生效，不必再改场景或 Inspector。
    [SerializeField] private string model = "glm-4-plus";
    [SerializeField] private string apiKeyEnvironmentVariable = "ARKCARD_LLM_API_KEY";
    [SerializeField, Min(1)] private int timeoutSeconds = 8;
    [SerializeField, Range(0f, 1f)] private float temperature = 0.1f;

    // [2] 对应《AI集成方案设计文档》3.5 节：角色 + 规则 + 返回格式约束
    private const string SystemPrompt =
        "你是一个类炉石传说卡牌对战游戏的AI战术顾问。你需要根据当前对局状态，制定最优的出牌策略。\n" +
        "游戏规则：\n" +
        "- 每回合费用自动增长，每回合抽一张卡牌。\n" +
        "- PlayMinion：从手牌打出随从，占用一个场地格子并进场；有目标时通常是战吼，不是法术。\n" +
        "- PlaySpell：从手牌打出法术，立即结算后进入墓地，不占用场地。\n" +
        "- 有无目标不能用来判断卡种：随从战吼可以指定目标，法术也可以是无目标效果。\n" +
        "- UseFieldCast：场上随从的主动技能，每回合一次。\n" +
        "- 随从可以攻击敌方随从或英雄（AttackMinion / AttackPlayer）。\n" +
        "- 目标：将敌方英雄血量降至0以下。\n" +
        "- 场地随从上限5格，手牌上限10张。\n" +
        "你只能从 legal_actions 中选择一个 action_index，不允许发明不存在的动作。\n" +
        "只返回 JSON，不要包含其他文字或 Markdown，结构必须为：\n" +
        "{\"action_index\":整数,\"strategy\":字符串,\"confidence\":0到1之间的小数}。";

    public IEnumerator RequestHint(
        BattleStateSnapshot state,
        System.Collections.Generic.IReadOnlyList<SimulatedAction> legalActions,
        Action<LLMStrategyHint> onComplete)
    {
        string apiKey = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey) || state == null || legalActions == null || legalActions.Count == 0)
        {
            // 降级方案：没有 Key 或没有合法动作时直接返回 null，调用方保留本地规划器结果
            onComplete?.Invoke(null);
            yield break;
        }

        // 【改动 2 配套 / 修复 HTTP 400】
        // 不能再用 JsonUtility.ToJson 序列化整个请求对象，它有两个硬限制：
        //   1) code 1210：float 无法精确表示十进制小数，
        //      temperature = 0.1f 会被打印成 "0.10000000149011612"，
        //      而智谱接口要求「temperature 参数限制小数点 2 位」；
        //   2) messages 变成 {}：JsonUtility.ToJson 对顶层数组会序列化成空对象，
        //      服务端报 "Cannot deserialize ArrayList<ChatMessage> from Object value"。
        // 因此改为手工拼装请求体（见 BuildRequestJson）。
        LLMChatRequest request = new()
        {
            model = model,
            messages = new[]
            {
                new LLMMessage { role = "system", content = SystemPrompt },
                new LLMMessage { role = "user", content = string.Empty }   // 内容在执行时手工填
            }
        };

        string userContent = JsonUtility.ToJson(BuildPrompt(state, legalActions));
        string requestJson = BuildRequestJson(request, temperature, SystemPrompt, userContent);

        using UnityWebRequest webRequest = new(endpoint, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(requestJson)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = Mathf.Max(1, timeoutSeconds)
        };
        webRequest.SetRequestHeader("Content-Type", "application/json");
        webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
        yield return webRequest.SendWebRequest();

        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            // 【改动 2 配套 / 排查用】原来的日志只有 HTTP 状态行，看不到服务端到底哪里不满意。
            // 服务端的错误详情都在响应体里（例如 400 时会给出 code 1210 说明是 temperature
            // 小数位问题），所以这里把请求体和响应体一并打印。
            Debug.LogWarning($"[AI LLM] Request failed: {webRequest.error}\n"
                + $"        请求体 = {requestJson}\n"
                + $"        响应体 = {webRequest.downloadHandler.text}");
            onComplete?.Invoke(null);   // 降级：网络失败/超时/参数错误 -> 丢弃建议
            yield break;
        }

        onComplete?.Invoke(ParseHint(webRequest.downloadHandler.text, legalActions.Count));
    }

    private static LLMStrategyPrompt BuildPrompt(
        BattleStateSnapshot state,
        System.Collections.Generic.IReadOnlyList<SimulatedAction> legalActions)
    {
        return LLMActionPromptBuilder.BuildPrompt(state, legalActions);
    }

    private static LLMStrategyHint ParseHint(string responseText, int legalActionCount)
    {
        try
        {
            LLMChatResponse response = JsonUtility.FromJson<LLMChatResponse>(responseText);
            string content = response != null && response.choices != null && response.choices.Length > 0
                && response.choices[0] != null && response.choices[0].message != null
                ? response.choices[0].message.content
                : string.Empty;

            // 【改动 2 配套 / 容错】有些模型（实测 glm-4-flash）会把 JSON 包在 Markdown
            // 代码块里返回，例如：
            //   "```json\n{\"action_index\": 1, ...}\n```"
            // JsonUtility.FromJson 无法解析这种字符串，会直接走 catch 分支丢弃建议。
            // 这里先剥掉代码块围栏，再解析；glm-4-plus / glm-4-air 返回的是干净 JSON，
            // 剥不剥都不受影响。
            string cleaned = StripCodeFence(content);
            LLMStrategyHint hint = JsonUtility.FromJson<LLMStrategyHint>(cleaned);
            if (hint == null || hint.action_index < 0 || hint.action_index >= legalActionCount)
            {
                return null;   // 降级：下标越界 -> 丢弃建议
            }
            hint.confidence = Mathf.Clamp01(hint.confidence);
            return hint;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AI LLM] Invalid JSON response: {exception.Message}");
            return null;       // 降级：JSON 解析失败 -> 丢弃建议
        }
    }

    /// <summary>
    /// 手工拼装请求体。必须手工拼的原因有两个，都是 JsonUtility 的硬限制：
    ///   1) temperature：float 无法精确表示十进制小数，JsonUtility 会打印完整精度
    ///      （0.1f -> 0.10000000149011612），服务端返回 code 1210「限制小数点[2]位」；
    ///   2) messages：JsonUtility.ToJson 对**顶层数组**会序列化成空对象 {}
    ///      （实测 "messages":{}），服务端报
    ///      "Cannot deserialize ArrayList&lt;ChatMessage&gt; from Object value"。
    /// 这里改为手工构造，model / messages / temperature 全部自己控制，
    /// 字符串统一走 EscapeJsonString，结构与转义都与标准 JSON 一致。
    /// </summary>
    private static string BuildRequestJson(LLMChatRequest request, float temperature, string systemPrompt, string userContent)
    {
        return "{\"model\":\"" + EscapeJsonString(request.model) + "\",\"messages\":["
            + "{\"role\":\"system\",\"content\":\"" + EscapeJsonString(systemPrompt) + "\"},"
            + "{\"role\":\"user\",\"content\":\"" + EscapeJsonString(userContent) + "\"}"
            + "],\"temperature\":"
            + temperature.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            + "}";
    }

    /// <summary>把字符串转义成可以安全嵌进 JSON 双引号里的形式。</summary>
    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new(value.Length + 16);
        foreach (char character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ')
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 去掉模型可能附加的 Markdown 代码块围栏，只留下 JSON 本体。
    /// 支持 ```json ... ```、``` ... ``` 以及前后带说明文字的情况。
    /// </summary>
    private static string StripCodeFence(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        string text = content.Trim();

        // 标准围栏：以 ``` 开头
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLineBreak = text.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                text = text.Substring(firstLineBreak + 1);          // 去掉 ```json 这一行
            }
            int closing = text.LastIndexOf("```", StringComparison.Ordinal);
            if (closing >= 0)
            {
                text = text.Substring(0, closing);
            }
            text = text.Trim();
        }

        // 兜底：前后可能混有说明文字，截取最外层花括号之间的内容
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            text = text.Substring(start, end - start + 1);
        }

        return text;
    }

#if UNITY_EDITOR
    // ========================================================================
    // 【改动 2 配套 / 仅编辑器】右键菜单独立调用测试
    //
    // 用途：不用打完整局，直接在 Inspector 上右键组件菜单执行一次真实 API 调用，
    //       把"请求 JSON + 响应 JSON + 解析结果"打到 Console，便于截图取证。
    //
    // 为什么需要它：游戏内触发 LLM 要求同时满足
    //   ① Inspector 的 Enable LLM Strategy 已勾选并【已保存场景】
    //   ② Unity 启动时继承了 ARKCARD_LLM_API_KEY
    //   ③ AI 正好在自己的回合做决策
    //   任一不满足就不会有 [AI Hybrid] 日志。用这个菜单可以绕开 ①③，只验证 API 链路。
    //
    // 注意：这条日志前缀是 [AI LLM]，不是游戏内决策路径的 [AI Hybrid]。
    //       两者都能证明 API 调用成功，但严格意义上路径不同，写文档时请如实说明。
    // ========================================================================
    [ContextMenu("ArkCard/测试 LLM 调用（仅编辑器）")]
    private void DebugCallLlmOnce()
    {
        string apiKey = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Debug.LogWarning($"[AI LLM] 环境变量 {apiKeyEnvironmentVariable} 未设置，测试取消。"
                + "请先设置该变量并从同一终端启动 Unity。");
            return;
        }

        // 用示意局面走正式 BuildPrompt，保证字段与游戏内请求一致
        BattleStateSnapshot debugState = CreateDebugSnapshot();
        System.Collections.Generic.List<SimulatedAction> debugActions = new()
        {
            new SimulatedAction { Type = SimulatedActionType.PlayHandCard, SourceCardId = 101 },
            new SimulatedAction
            {
                Type = SimulatedActionType.AttackPlayer,
                SourceCardId = 201,
                Targets = new System.Collections.Generic.List<SimulatedTarget> { SimulatedTarget.Player(1) },
            },
            new SimulatedAction { Type = SimulatedActionType.EndTurn },
        };
        LLMStrategyPrompt testPrompt = BuildPrompt(debugState, debugActions);
        string userJson = JsonUtility.ToJson(testPrompt);
        LLMChatRequest testRequest = new()
        {
            model = model,
            messages = new[]
            {
                new LLMMessage { role = "system", content = SystemPrompt },
                new LLMMessage { role = "user", content = userJson },
            },
        };
        // 与游戏内路径用同一个构造函数：temperature 固定 2 位小数、messages 手工拼成真数组
        string requestJson = BuildRequestJson(testRequest, temperature, SystemPrompt, userJson);

        using UnityWebRequest webRequest = new(endpoint, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(requestJson)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = Mathf.Max(1, timeoutSeconds)
        };
        webRequest.SetRequestHeader("Content-Type", "application/json");
        webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);

        Debug.Log($"[AI LLM] ===== 独立调用测试开始 =====\n"
            + $"endpoint = {endpoint}\nmodel = {model}\n"
            + $"system prompt = {SystemPrompt}\n"
            + $"user content = {userJson}\n"
            + $"请求体 = {requestJson}");

        webRequest.SendWebRequest().completed += _ =>
        {
            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                // 降级路径：与游戏内一致，打印失败原因并放弃本次建议
                Debug.LogWarning($"[AI LLM] 请求失败（游戏内会回退本地规划器）：{webRequest.error}\n"
                    + $"响应体 = {webRequest.downloadHandler.text}");
                return;
            }

            string responseText = webRequest.downloadHandler.text;
            Debug.Log($"[AI LLM] 响应体 = {responseText}");

            LLMStrategyHint hint = ParseHint(responseText, testPrompt.legal_actions.Length);
            if (hint == null)
            {
                Debug.LogWarning("[AI LLM] 解析失败或下标越界（游戏内会丢弃该建议，保留本地 MCTS 结果）。");
                return;
            }

            Debug.Log($"[AI LLM] 解析结果：action_index = {hint.action_index}, "
                + $"confidence = {hint.confidence:0.00}, strategy = {hint.strategy}\n"
                + $"[AI LLM] 是否会被游戏采纳："
                + (hint.confidence >= 0.6f ? "会采纳（confidence 达到阈值）" : "不会采纳（confidence 低于阈值）"));
        };
    }

    private static BattleStateSnapshot CreateDebugSnapshot()
    {
        CardStateSnapshot minion = new()
        {
            RuntimeId = 101,
            OwnerIndex = 0,
            State = CardState.Hand,
            Cost = 3,
            Attack = 2,
            Health = 3,
            Data = new CardData
            {
                name = "近卫",
                cardType = CardType.Minion,
                cost = 3,
                attack = 2,
                health = 3,
                effectDescription = "嘲讽",
                passiveTypes = new System.Collections.Generic.List<PassiveType> { PassiveType.Guard },
            },
        };
        CardStateSnapshot attacker = new()
        {
            RuntimeId = 201,
            OwnerIndex = 0,
            State = CardState.Field,
            Cost = 2,
            Attack = 3,
            Health = 2,
            Data = new CardData
            {
                name = "先锋",
                cardType = CardType.Minion,
                cost = 2,
                attack = 3,
                health = 2,
            },
        };
        return new BattleStateSnapshot
        {
            CurrentPlayerIndex = 0,
            Players = new System.Collections.Generic.List<PlayerStateSnapshot>
            {
                new()
                {
                    PlayerIndex = 0,
                    Health = 22,
                    MaxHealth = 30,
                    Cost = 6,
                    MaxCost = 6,
                    Hand = new System.Collections.Generic.List<CardStateSnapshot> { minion },
                    Field = new System.Collections.Generic.List<CardStateSnapshot> { attacker },
                },
                new()
                {
                    PlayerIndex = 1,
                    Health = 18,
                    MaxHealth = 30,
                    Cost = 6,
                    MaxCost = 6,
                },
            },
        };
    }
#endif
}
