using System.Text.Json;
using System.Text.Json.Serialization;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Bridge;

/// <summary>画面 → C# の要求。{ id, command, payload }</summary>
public sealed record BridgeRequest(string Id, string Command, JsonElement Payload);

/// <summary>C# → 画面 の応答。{ id, ok, result | error }</summary>
public sealed record BridgeResponse(string Id, bool Ok, object? Result, string? Error);

/// <summary>JSON の共通設定（camelCase・enum は文字列・日本語はエスケープしない）。</summary>
public static class BridgeJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static T Read<T>(JsonElement payload) where T : new() =>
        payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new T()
            : payload.Deserialize<T>(Options) ?? new T();
}

// ── 各コマンドの入出力 ──

/// <summary>設定画面に渡す設定（パスワードは返さない）。</summary>
public sealed class SettingsView
{
    public DbSettingsView Db { get; set; } = new();
    public LlmSettings Llm { get; set; } = new();
}

public sealed class DbSettingsView
{
    public string Server { get; set; } = "";
    public string Database { get; set; } = "";
    public bool UseWindowsAuth { get; set; } = true;
    public string User { get; set; } = "";

    /// <summary>画面からの入力。null は「変更しない」、空文字は「削除」。</summary>
    public string? Password { get; set; }

    /// <summary>画面への表示用: パスワードが保存済みか。</summary>
    public bool HasPassword { get; set; }

    public bool TrustServerCertificate { get; set; } = true;
    public int CommandTimeoutSec { get; set; } = 120;
    public TableNames Tables { get; set; } = new();
}

public sealed class LoadDataRequest
{
    public StructureType StructureType { get; set; } = StructureType.RC;
}

public sealed class ProjectIdRequest
{
    public string ProjectId { get; set; } = "";
}

public sealed class TextRequest
{
    public string Text { get; set; } = "";
}

public sealed class DbTablesRequest
{
    /// <summary>true: DB 構造（テーブル一覧・関連）を取り直す</summary>
    public bool Refresh { get; set; }
}

public sealed class DbTableRequest
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = "";
}

public sealed class InsightRequest
{
    /// <summary>summary / review / project</summary>
    public string Kind { get; set; } = "summary";
}

public sealed record LoadDataResult(
    int ProjectCount, int AssessCount, int ReviseCount, int MaterialCount, string? Warning, AnalysisResult Result);

public sealed record KeyValueItem(string Key, object? Value);

public sealed record ProjectDetailResult(
    string ProjectId, string? Name, List<KeyValueItem> Attributes,
    double? ReviseYenPerM2, double? MedianYenPerM2, double? MedianRatioPercent, string Explanation);

/// <summary>チャット・AI 条件設定の応答。Messages は吹き出しとして順に表示する。</summary>
public sealed record ChatResult(string Intent, List<ChatBubble> Messages, AnalysisResult? Result)
{
    /// <summary>DB を調べて答えたとき: 参照したテーブル（チャットに「DB編集で開く」を出す）</summary>
    public ChatTableLink? Table { get; init; }
}

/// <summary>チャットから DB編集で開くテーブル（Search は検索欄に入れる語）。</summary>
public sealed record ChatTableLink(string Schema, string Name, string Label, string? Search);

/// <summary>Role: assistant / system</summary>
public sealed record ChatBubble(string Role, string Text);
