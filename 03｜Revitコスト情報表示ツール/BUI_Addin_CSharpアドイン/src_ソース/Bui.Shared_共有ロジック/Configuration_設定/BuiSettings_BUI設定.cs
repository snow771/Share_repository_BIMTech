// このファイルはCSVとローカルLLMの設定形式を、画面・保存・起動処理で共有します。
// CSV列定義に依存し、項目名の変更は既存の設定JSONと画面入力の互換性に影響します。
using Bui.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bui.Configuration;

public sealed record DataSettings(
    string Provider,
    // 製品データはGoldStandardスキーマの複数CSVをフォルダ単位で扱います。
    // 1枚のCSVパスではなく、Products.csvなどが入ったフォルダを指します。
    string DataFolderPath,
    string PriceCsvPath,
    string AssemblyCsvPath,
    string? IdAliasCsvPath,
    CsvColumnMapping ProductColumns,
    PriceColumnMapping PriceColumns,
    // 以下は候補選択・新規登録用。実データのCSV列名（カテゴリー、用途など）に依存するため、
    // 定数にせず設定へ出します。SQL Server移行時など列名が変わってもコード修正が不要になります。
    IReadOnlyList<string> CandidateFilterColumns,
    IReadOnlyList<string> NewProductColumns,
    IReadOnlyList<string> NewProductRequiredColumns,
    IReadOnlyList<string> DuplicateCompareColumns,
    string NewProductIdPrefix,
    int NewProductIdDigits,
    // SQL Serverをデータ元にするときの接続先です。Windows認証なので秘密値はありません。
    string SqlServerName = "",
    string SqlDatabaseName = "",
    // 表記ゆれ.csv のパスです。未設定なら表記ゆれを使いません（検索は動きます）。
    // 構成CSVと同じく、SQL接続中もこのファイルを使います。
    string? SynonymCsvPath = null);

public sealed record LlmSettings(
    bool Enabled,
    string Provider,
    string BaseUrl,
    string Model,
    int TimeoutSeconds,
    string? ApiKey);

public sealed record BuiSettings(DataSettings Data, LlmSettings Llm);

/// <summary>設定と、起動を妨げない警告のみをまとめます。</summary>
public sealed record SettingsLoadResult(BuiSettings Settings, IReadOnlyList<string> Warnings);

/// <summary>秘密ファイルから受け取ったAPI keyと、非許可項目の有無だけを持ちます。</summary>
internal sealed record SecretSettings(string? ApiKey, bool HasIgnoredFields);

// 認証ファイルをBuiSettings全体として読まず、許可したAPI keyだけを受け取る専用DTOです。
internal sealed class SecretFileDto
{
    public SecretLlmDto? Llm { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? IgnoredRootFields { get; init; }
}

internal sealed class SecretLlmDto
{
    public string? ApiKey { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? IgnoredLlmFields { get; init; }
}
