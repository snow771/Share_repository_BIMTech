using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Settings;

namespace SimilaritySearchApp.Ocr.Azure;

/// <summary>
/// Azure AI Document Intelligence の prebuilt-layout（REST API 2024-11-30）を呼ぶ。
/// Python 版の azure-ai-documentintelligence 1.0 と同じ API なので、結果 JSON（analyzeResult）も同じ形
/// （pages / tables / paragraphs ... の camelCase）。
///
/// 流れ: PDF を POST → 202 と Operation-Location が返る → そこを完了するまで GET。
/// 429（呼び出し回数の制限）は Retry-After だけ待って数回やり直す。
/// </summary>
public sealed class AzureLayoutClient : IDisposable
{
    public const string ApiVersion = "2024-11-30";
    private const int MaxRetry = 5;

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _key;

    public AzureLayoutClient(string endpoint, string key, TimeSpan? timeout = null)
    {
        _endpoint = endpoint.Trim().TrimEnd('/');
        _key = key.Trim();
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromMinutes(5) };
    }

    /// <summary>設定（空なら環境変数 DI_ENDPOINT / DI_KEY）から作る。どちらも無ければ説明付きで例外。</summary>
    public static AzureLayoutClient FromSettings(OcrSettings s)
    {
        var (endpoint, key, _) = ResolveCredentials(s);
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "Azure の接続先（エンドポイント）またはキーが設定されていません。画面右上の「⚙ Azure 設定」で入力してください" +
                "（環境変数 DI_ENDPOINT / DI_KEY を設定している場合はそれも使えます）。");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"エンドポイントの形式が正しくありません: {endpoint}（例: https://xxxx.cognitiveservices.azure.com/）");
        return new AzureLayoutClient(endpoint, key);
    }

    /// <summary>使う接続先とキー。Source は「設定」/「環境変数」/「未設定」。</summary>
    public static (string Endpoint, string Key, string Source) ResolveCredentials(OcrSettings s)
    {
        var key = SettingsStore.Unprotect(s.EncryptedKey);
        if (!string.IsNullOrWhiteSpace(s.Endpoint) && !string.IsNullOrWhiteSpace(key))
            return (s.Endpoint.Trim(), key, "設定");
        var envEndpoint = Environment.GetEnvironmentVariable("DI_ENDPOINT")?.Trim() ?? "";
        var envKey = Environment.GetEnvironmentVariable("DI_KEY")?.Trim() ?? "";
        var endpoint = string.IsNullOrWhiteSpace(s.Endpoint) ? envEndpoint : s.Endpoint.Trim();
        key = string.IsNullOrWhiteSpace(key) ? envKey : key;
        var source = string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key) ? "未設定" : "環境変数";
        return (endpoint, key, source);
    }

    /// <summary>接続テスト（リソース情報を取得するだけ。ページは消費しない）。</summary>
    public async Task<string> TestAsync(CancellationToken ct = default)
    {
        var url = $"{_endpoint}/documentintelligence/info?api-version={ApiVersion}";
        using var res = await SendWithRetryAsync(() => NewRequest(HttpMethod.Get, url), ct);
        await EnsureSuccessAsync(res, ct);
        return "Azure AI Document Intelligence に接続できました（ページは消費していません）。";
    }

    /// <summary>PDF（数ページ分）を解析して analyzeResult を返す。</summary>
    public async Task<JsonObject> AnalyzeLayoutAsync(byte[] pdf, CancellationToken ct = default)
    {
        var url = $"{_endpoint}/documentintelligence/documentModels/prebuilt-layout:analyze?api-version={ApiVersion}";
        HttpRequestMessage Post()
        {
            var r = NewRequest(HttpMethod.Post, url);
            r.Content = new ByteArrayContent(pdf);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return r;
        }

        string operation;
        using (var res = await SendWithRetryAsync(Post, ct))
        {
            await EnsureSuccessAsync(res, ct);
            operation = res.Headers.TryGetValues("Operation-Location", out var v) ? v.First()
                : throw new InvalidOperationException("Azure の応答に Operation-Location がありません。");
        }

        // 完了を待つ（Retry-After が無ければ 1 秒ごと）
        var started = DateTime.UtcNow;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var res = await SendWithRetryAsync(() => NewRequest(HttpMethod.Get, operation), ct);
            await EnsureSuccessAsync(res, ct);
            var body = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))?.AsObject()
                       ?? throw new InvalidOperationException("Azure の応答を読み取れませんでした。");
            var status = (string?)body["status"];
            switch (status)
            {
                case "succeeded":
                    return body["analyzeResult"]?.AsObject() ?? new JsonObject();
                case "failed":
                case "canceled":
                    throw new InvalidOperationException($"Azure の解析に失敗しました: {ErrorText(body["error"])}");
            }
            if (DateTime.UtcNow - started > TimeSpan.FromMinutes(10))
                throw new TimeoutException("Azure の解析が 10 分以上終わりませんでした。");
            await Task.Delay(RetryAfter(res) ?? TimeSpan.FromSeconds(1), ct);
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.Add("Ocp-Apim-Subscription-Key", _key);
        return r;
    }

    /// <summary>429（回数制限）・503 は待ってやり直す。</summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = make();
            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req, ct);
            }
            catch (HttpRequestException e)
            {
                throw new InvalidOperationException($"Azure に接続できませんでした（エンドポイント・ネットワークを確認してください）: {e.Message}", e);
            }
            if (res.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable) || attempt >= MaxRetry) return res;
            var wait = RetryAfter(res) ?? TimeSpan.FromSeconds(Math.Min(30, 2 * attempt));
            res.Dispose();
            await Task.Delay(wait, ct);
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage res)
    {
        if (res.Headers.RetryAfter?.Delta is TimeSpan d) return d;
        if (res.Headers.TryGetValues("retry-after-ms", out var ms) && int.TryParse(ms.First(), out var n)) return TimeSpan.FromMilliseconds(n);
        return null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var text = await res.Content.ReadAsStringAsync(ct);
        string detail;
        try
        {
            detail = ErrorText(JsonNode.Parse(text)?["error"]);
        }
        catch (Exception)
        {
            detail = text.Length > 300 ? text[..300] : text;
        }
        var hint = (int)res.StatusCode switch
        {
            401 => "キーが正しくありません。",
            403 => "利用できる枚数・権限の上限に達している可能性があります（無料枠の上限など）。",
            404 => "エンドポイントが正しくありません。",
            429 => "呼び出し回数の制限に達しました。しばらく待ってから実行してください。",
            _ => "",
        };
        throw new InvalidOperationException($"Azure がエラーを返しました（{(int)res.StatusCode}）。{hint} {detail}".Trim());
    }

    private static string ErrorText(JsonNode? error)
    {
        if (error is null) return "";
        var code = (string?)error["code"];
        var message = (string?)error["message"];
        var inner = (string?)error["innererror"]?["message"];
        return string.Join(" ", new[] { code, message, inner }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    public void Dispose() => _http.Dispose();
}
