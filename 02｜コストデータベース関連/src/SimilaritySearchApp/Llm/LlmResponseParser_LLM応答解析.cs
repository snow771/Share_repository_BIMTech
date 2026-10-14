using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SimilaritySearchApp.Llm;

/// <summary>LLM へ渡す1メッセージ。Role は system / user / assistant。</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// Ollama 応答の解析（Python の safe_json_loads / strip_think_blocks / _post_json_raw の後半 /
/// _extract_ollama_text / _messages_with_no_think）。
/// </summary>
public static class LlmResponseParser
{
    public const string NoThinkPrefix = "/no_think\nJSONや回答本文のみを返してください。途中の思考は出力しないでください。";

    private static readonly Regex CodeBlock = new(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ThinkBlock = new(@"<think>.*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>応答文から JSON オブジェクトを取り出す。コードフェンスや前置き文にも対応。</summary>
    public static JsonObject ParseJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("AI応答からJSONを解釈できませんでした。");
        var body = text.Trim();

        if (TryParseObject(body, out var obj)) return obj;

        var m = CodeBlock.Match(body);
        if (m.Success && TryParseObject(m.Groups[1].Value.Trim(), out obj)) return obj;

        var start = body.IndexOf('{');
        var end = body.LastIndexOf('}');
        if (start >= 0 && end > start && TryParseObject(body[start..(end + 1)], out obj)) return obj;

        throw new InvalidOperationException("AI応答からJSONを解釈できませんでした。");
    }

    public static string StripThink(string text) =>
        string.IsNullOrEmpty(text) ? "" : ThinkBlock.Replace(text, "").Trim();

    /// <summary>
    /// HTTP 応答本文を解析する。単一 JSON ならそのまま、NDJSON（1行1JSON）なら結合して
    /// 本文（response / message.content）を連結した "response" を持つオブジェクトにする。
    /// </summary>
    public static JsonObject ParseBody(string body)
    {
        if (TryParseObject(body, out var single)) return single;

        var merged = new JsonObject();
        var parts = new List<string>();
        var any = false;
        foreach (var line in body.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || !TryParseObject(t, out var obj)) continue;
            any = true;

            var response = obj["response"] is JsonValue rv && rv.TryGetValue<string>(out var rs) ? rs : null;
            if (!string.IsNullOrEmpty(response))
            {
                parts.Add(response);
            }
            else if (obj["message"] is JsonObject msg && msg["content"] is JsonValue cv
                     && cv.TryGetValue<string>(out var cs) && !string.IsNullOrEmpty(cs))
            {
                parts.Add(cs);
            }

            foreach (var kv in obj)
                merged[kv.Key] = kv.Value?.DeepClone();
        }

        if (!any)
        {
            var head = body.Length > 2000 ? body[..2000] : body;
            throw new InvalidOperationException($"Ollama応答をJSONとして解釈できませんでした。\nRAW:\n{head}");
        }
        if (parts.Count > 0) merged["response"] = string.Concat(parts);
        return merged;
    }

    /// <summary>本文を取り出す: message.content → response → choices[0].message.content / text。</summary>
    public static string ExtractText(JsonObject parsed)
    {
        if (parsed["message"] is JsonObject msg && Str(msg["content"]) is { Length: > 0 } c) return c.Trim();
        if (Str(parsed["response"]) is { Length: > 0 } r) return r.Trim();
        if (parsed["choices"] is JsonArray { Count: > 0 } choices && choices[0] is JsonObject ch0)
        {
            if (ch0["message"] is JsonObject m2 && Str(m2["content"]) is { Length: > 0 } c2) return c2.Trim();
            if (Str(ch0["text"]) is { Length: > 0 } t) return t.Trim();
        }
        return "";
    }

    /// <summary>最初の system メッセージの先頭に /no_think 指示を付ける（無ければ system を追加）。</summary>
    public static List<ChatMessage> WithNoThink(IReadOnlyList<ChatMessage> messages)
    {
        var list = messages.ToList();
        var idx = list.FindIndex(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            var content = list[idx].Content ?? "";
            if (!content.Contains("/no_think", StringComparison.Ordinal))
                list[idx] = list[idx] with { Content = NoThinkPrefix + "\n" + content };
        }
        else
        {
            list.Insert(0, new ChatMessage("system", NoThinkPrefix));
        }
        return list;
    }

    // ------------------------------------------------------------------

    private static bool TryParseObject(string text, out JsonObject obj)
    {
        try
        {
            if (JsonNode.Parse(text) is JsonObject o)
            {
                obj = o;
                return true;
            }
        }
        catch (JsonException)
        {
        }
        obj = null!;
        return false;
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
