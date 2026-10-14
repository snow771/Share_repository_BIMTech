using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Llm;

/// <summary>
/// ローカル LLM（Ollama）クライアント（Python の call_ollama / call_ollama_json）。
/// /api/chat を優先し、404 や本文が空のときは /api/generate に切り替える。
/// </summary>
public sealed class OllamaClient
{
    private readonly LlmSettings _settings;
    private readonly HttpClient _http;

    public OllamaClient(LlmSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _http = http ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, settings.TimeoutSec));
    }

    private string BaseUrl => _settings.BaseUrl.Trim().TrimEnd('/');

    /// <summary>テキスト応答を返す。format に JSON スキーマを渡すと構造化出力になる。</summary>
    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages, double? temperature = null, JsonNode? format = null, CancellationToken ct = default)
    {
        var model = _settings.Model;
        var safe = LlmResponseParser.WithNoThink(messages);
        var options = new JsonObject
        {
            ["temperature"] = temperature ?? _settings.Temperature,
            ["num_predict"] = _settings.NumPredict,
        };
        if (_settings.NumCtx > 0) options["num_ctx"] = _settings.NumCtx;

        var chatPayload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(safe.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }).ToArray()),
            ["stream"] = false,
            ["think"] = false,
            ["keep_alive"] = "10m",
            ["options"] = options.DeepClone(),
        };
        var generatePayload = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = string.Join("\n", safe.Select(m => $"[{m.Role}] {m.Content}")),
            ["stream"] = false,
            ["think"] = false,
            ["keep_alive"] = "10m",
            ["options"] = options.DeepClone(),
        };
        if (format is not null)
        {
            chatPayload["format"] = format.DeepClone();
            generatePayload["format"] = format.DeepClone();
        }

        string lastErr;

        // ── 1) /api/chat ──
        try
        {
            var (parsed, raw) = await PostAsync($"{BaseUrl}/api/chat", chatPayload, ct);
            var content = LlmResponseParser.ExtractText(parsed);
            if (content.Length > 0) return content;
            lastErr = parsed["thinking"] is not null
                ? "/api/chat は thinking のみを返し、本文が空でした。\nRAW:\n" + Head(raw, 1500)
                : "/api/chat 応答に本文がありません。\nRAW:\n" + Head(raw, 1500);
        }
        catch (OllamaHttpException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            lastErr = "/api/chat が見つかりません (HTTP 404)";
        }
        catch (OllamaHttpException e) when (IsContextOverflow(e.Detail))
        {
            throw new LlmContextOverflowException(
                $"指示文が LLM のコンテキスト長を超えました（設定 {_settings.NumCtx} トークン）。設定 → ローカルLLM の「コンテキスト長」を大きくしてください。\n{e.Detail}");
        }
        catch (OllamaHttpException e)
        {
            throw new InvalidOperationException($"Ollama接続エラー(api/chat): HTTP {(int)e.StatusCode}\n{e.Detail}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw TimeoutError(model);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"Ollama接続エラー: {e.Message}");
        }
        catch (InvalidOperationException e)
        {
            lastErr = e.Message;
        }

        // ── 2) /api/generate へフォールバック ──
        try
        {
            var (parsed, raw) = await PostAsync($"{BaseUrl}/api/generate", generatePayload, ct);
            var content = LlmResponseParser.ExtractText(parsed);
            if (content.Length > 0) return content;
            if (parsed["thinking"] is not null)
            {
                throw new InvalidOperationException(
                    "Ollamaの/api/generate応答が thinking のみで、本文(response)が空でした。\n" +
                    "対処案: 非thinking系モデルへ切り替えてください。\nRAW:\n" + Head(raw, 2000));
            }
            throw new InvalidOperationException("Ollamaの/api/generate応答から本文を取得できませんでした。\nRAW:\n" + Head(raw, 2000));
        }
        catch (OllamaHttpException e)
        {
            throw new InvalidOperationException(
                $"Ollama接続エラー(api/chat→api/generate fallback失敗): {lastErr}\nHTTP {(int)e.StatusCode}\n{e.Detail}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw TimeoutError(model);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"Ollama接続エラー(api/generate): {e.Message}");
        }
    }

    /// <summary>JSON スキーマ付きで問い合わせ、JSON オブジェクトとして返す。</summary>
    public async Task<JsonObject> ChatJsonAsync(
        IReadOnlyList<ChatMessage> messages, JsonNode schema, double temperature = 0.0, CancellationToken ct = default)
    {
        var raw = await ChatAsync(messages, temperature, schema, ct);
        return LlmResponseParser.ParseJsonObject(LlmResponseParser.StripThink(raw));
    }

    /// <summary>インストール済みモデル名の一覧（GET /api/tags）。</summary>
    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"{BaseUrl}/api/tags", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"モデル一覧の取得に失敗しました: HTTP {(int)resp.StatusCode}\n{Head(body, 500)}");

        var root = JsonNode.Parse(body) as JsonObject;
        var names = new List<string>();
        if (root?["models"] is JsonArray arr)
        {
            foreach (var m in arr)
                if (m?["name"] is JsonValue v && v.TryGetValue<string>(out var name)) names.Add(name);
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>接続テスト。サーバー到達とモデルの有無を確認する。</summary>
    public async Task<string> TestAsync(CancellationToken ct = default)
    {
        var models = await ListModelsAsync(ct);
        return models.Contains(_settings.Model)
            ? $"接続OK: モデル「{_settings.Model}」を確認しました（全{models.Count}モデル）。"
            : $"接続OK（全{models.Count}モデル）。ただしモデル「{_settings.Model}」が見つかりません。ollama pull で取得してください。";
    }

    // ------------------------------------------------------------------

    private async Task<(JsonObject Parsed, string Raw)> PostAsync(string url, JsonObject payload, CancellationToken ct)
    {
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(url, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new OllamaHttpException(resp.StatusCode, body);
        return (LlmResponseParser.ParseBody(body), body);
    }

    private InvalidOperationException TimeoutError(string model) => new(
        $"Ollama応答タイムアウト: {_settings.TimeoutSec}秒以内に応答がありません。" +
        $" 初回ロード中の可能性があるため、先に ollama run {model} を一度実行してください。");

    private static string Head(string s, int n) => s.Length > n ? s[..n] : s;

    /// <summary>「入力がコンテキスト長を超えた」エラーか（Ollama / llama.cpp の両方の書き方に対応）。</summary>
    internal static bool IsContextOverflow(string detail) =>
        detail.Contains("exceed_context_size", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("context length", StringComparison.OrdinalIgnoreCase) && detail.Contains("exceed", StringComparison.OrdinalIgnoreCase);

    private sealed class OllamaHttpException : Exception
    {
        public OllamaHttpException(HttpStatusCode code, string detail) : base($"HTTP {(int)code}")
        {
            StatusCode = code;
            Detail = detail;
        }

        public HttpStatusCode StatusCode { get; }
        public string Detail { get; }
    }
}

/// <summary>指示文が LLM のコンテキスト長を超えた（指示文を短くしてやり直せる）。</summary>
public sealed class LlmContextOverflowException : InvalidOperationException
{
    public LlmContextOverflowException(string message) : base(message)
    {
    }
}
