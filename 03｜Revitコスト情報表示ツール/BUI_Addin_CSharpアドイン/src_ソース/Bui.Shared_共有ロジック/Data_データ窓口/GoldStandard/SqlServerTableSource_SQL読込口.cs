// このファイルの目的:
//   SQL Serverから11枚の表を読み、CSVと同じ形の表一式にします。
//
// なぜここにSQLの型が出てこないのか:
//   Bui.SharedはRevitにもSQLのパッケージにも依存させません。
//   実際の接続はISqlTableReaderの実装（Bui.Addin側）が担い、
//   ここは「表名を渡すと表が返る」という細い境界だけを使います。
//   おかげでSQL Serverの無いこのPCでも変換規則を単体テストできます。
//
// 修正時の影響:
//   NULLは必ず空文字にしてください。CSVの空欄と揃えることで、
//   平坦化以降が出どころを気にせず済みます。
//   接続に失敗しても例外を投げず、Problemsへ理由を入れてください。
//   投げるとBUIパネルごと止まります。

namespace Bui.Data.GoldStandard;

/// <summary>表名を渡すと1枚の表を返す、SQL接続の細い境界です。</summary>
internal interface ISqlTableReader : IDisposable
{
    /// <summary>表が無ければnullを返します。例外は投げません。</summary>
    GoldStandardTable? Read(string table);
}

/// <summary>SQL Serverから表一式を読みます。読み取り専用です。</summary>
internal sealed class SqlServerTableSource : IGoldStandardTableSource
{
    private readonly Func<ISqlTableReader> _openReader;
    private readonly string _serverName;
    private readonly string _databaseName;
    private long _version;

    public SqlServerTableSource(
        Func<ISqlTableReader> openReader,
        string dataFolderPath,
        string serverName,
        string databaseName)
    {
        _openReader = openReader ?? throw new ArgumentNullException(nameof(openReader));
        DataFolderPath = dataFolderPath ?? string.Empty;
        _serverName = serverName ?? string.Empty;
        _databaseName = databaseName ?? string.Empty;
    }

    /// <summary>製品画像の置き場です。SQL接続中も画像はローカルのファイルとして扱います。</summary>
    public string DataFolderPath { get; }

    /// <summary>SQLは読み取り専用です（設計 §1）。</summary>
    public bool SupportsWrite => false;

    /// <summary>CSVフォルダではありません。単価CSVを読みに行かせないための印です。</summary>
    public bool IsCsvFolder => false;

    public long Version => Volatile.Read(ref _version);

    public string Describe() => $"SQL Server: {_serverName} / {_databaseName}";

    public GoldStandardTableSet Load()
    {
        var tables = new Dictionary<string, GoldStandardTable>(StringComparer.Ordinal);
        var problems = new List<string>();
        // 「サーバーに届かなかった」のか「届いたがテーブルが無い」のかを区別します。
        // どちらも同じ文言で報告していたため、接続できていないときにも
        // 「Productsテーブルを読み取れない」と出て、利用者は存在するはずのテーブルを
        // 探し続けることになりました。原因の種類が違えば、次にやることも違います。
        var connectionFailed = false;

        try
        {
            using var reader = _openReader();
            foreach (var table in GoldStandardTables.All)
            {
                try
                {
                    var value = reader.Read(table);
                    if (value is null)
                    {
                        problems.Add($"{table} テーブルがありません。この表は空として扱います。");
                        continue;
                    }

                    tables[table] = value;
                }
                catch (Exception exception)
                {
                    problems.Add($"{table} テーブルを読み取れません。{exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            // 接続そのものに失敗した場合です。ここで投げるとBUIパネルごと止まります。
            // 接続の理由が唯一かつ最重要なので、先頭に置きます。
            connectionFailed = true;
            problems.Insert(0, exception.Message);
        }

        // 接続に失敗したときは、ここで要約を足しません。
        // 足すと「テーブルが読み取れない」が先頭に来て、本当の理由（サーバーに届かない）を
        // 押し下げてしまいます。接続の理由そのものが既に先頭にあります。
        if (!tables.ContainsKey(GoldStandardTables.Products) && !connectionFailed)
        {
            problems.Insert(
                0,
                $"SQL Serverへは接続できましたが、{GoldStandardTables.Products} テーブルを読み取れませんでした。"
                    + "データベース名が正しいか、GoldStandard_SQLServer_FullBuild_v2.sql を実行済みか、"
                    + "このWindowsユーザーに参照権限があるかを確認してください。");
        }

        return new GoldStandardTableSet(tables, problems);
    }
}
