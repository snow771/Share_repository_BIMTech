// このファイルは出荷値、PC個別値、認証値の優先順位を一か所で守ります。
// BuiSettingsに依存し、ここを変えるとどのPC設定が有効になるかに影響します。
namespace Bui.Configuration;

public static class SettingsMerger
{
    internal static SettingsLoadResult Merge(BuiSettings defaults, BuiSettings? local, SecretSettings? secrets)
    {
        // localは非秘密設定全体を置き換えますが、API keyだけは必ずsecrets由来に限定します。
        // ただし「置き換える」といっても、localに存在しない項目まで消してはいけません。
        // 保存済みのSettings.Local_個別設定.jsonは、候補選択・新規登録の6項目が
        // 追加される前に書かれている場合があります。その場合これらはnull（数値は0）で
        // 読み込まれ、画面の初期化が起動時に例外で落ちてBUI自体が開かなくなります。
        // 不足分は出荷設定で埋めます。
        var nonSecret = local is null
            ? defaults with { Data = FillMissingData(defaults.Data, defaults.Data) }
            : local with { Data = FillMissingData(local.Data, defaults.Data) };
        var apiKey = secrets?.ApiKey;
        // CSVパスは「パスのコピー」で引用符が付くことがあるため、読み込み時点で整えます。
        // 保存済みの値が壊れていても、ここを通ることで自動的に直ります。
        var merged = nonSecret with
        {
            Data = CsvPathNormalizer.Normalize(nonSecret.Data),
            Llm = nonSecret.Llm with { ApiKey = apiKey }
        };
        // 未知フィールド名そのものに秘密値が埋め込まれる可能性があるため、名前は表示しません。
        var warnings = secrets?.HasIgnoredFields == true
            ? new[] { "認証情報ファイルのAPI key以外の項目は無視しました。" }
            : Array.Empty<string>();
        return new SettingsLoadResult(merged, warnings);
    }

    /// <summary>
    /// 古いPC個別設定に無い項目を出荷設定で補います。
    /// 「キー自体が無い」場合も「キーはあるが値がnull」の場合も、どちらもnullとして読まれるため
    /// 同じ扱いで補えます。桁数は0だと採番が必ず失敗するため、1未満を未設定とみなします。
    /// ここを外すと、更新前から設定ファイルを持っている利用者のBUIが起動時に開かなくなります。
    /// </summary>
    private static DataSettings FillMissingData(DataSettings local, DataSettings defaults) => local with
    {
        // 更新前の設定ファイルにはdataFolderPathがありません。nullのままだと画面の初期化が落ちます。
        DataFolderPath = string.IsNullOrWhiteSpace(local.DataFolderPath)
            ? defaults.DataFolderPath ?? string.Empty
            : local.DataFolderPath,
        // 更新前の設定ファイルはprovider="Csv"を保存しています。
        // 読み替えないとDataProviderPolicyが起動時に例外を投げ、BUIが開かなくなります。
        Provider = string.IsNullOrWhiteSpace(local.Provider)
            || string.Equals(local.Provider, "Csv", StringComparison.OrdinalIgnoreCase)
                ? "GoldStandard"
                : local.Provider,
        CandidateFilterColumns = local.CandidateFilterColumns ?? defaults.CandidateFilterColumns,
        NewProductColumns = local.NewProductColumns ?? defaults.NewProductColumns,
        NewProductRequiredColumns = local.NewProductRequiredColumns ?? defaults.NewProductRequiredColumns,
        DuplicateCompareColumns = local.DuplicateCompareColumns ?? defaults.DuplicateCompareColumns,
        NewProductIdPrefix = string.IsNullOrWhiteSpace(local.NewProductIdPrefix)
            ? defaults.NewProductIdPrefix
            : local.NewProductIdPrefix,
        NewProductIdDigits = local.NewProductIdDigits < 1 ? defaults.NewProductIdDigits : local.NewProductIdDigits,
        // 更新前の設定ファイルにはこの2項目がありません。空のまま扱い、設定画面で入力させます。
        SqlServerName = string.IsNullOrWhiteSpace(local.SqlServerName)
            ? defaults.SqlServerName ?? string.Empty
            : local.SqlServerName,
        SqlDatabaseName = string.IsNullOrWhiteSpace(local.SqlDatabaseName)
            ? defaults.SqlDatabaseName ?? string.Empty
            : local.SqlDatabaseName
    };
}
