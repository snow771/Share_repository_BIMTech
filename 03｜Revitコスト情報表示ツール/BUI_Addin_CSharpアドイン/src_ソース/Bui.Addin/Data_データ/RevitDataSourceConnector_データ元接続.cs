// このファイルの目的:
//   設定タブの操作を、実際のデータ元の差し替えにつなげます。
//
// なぜ起動経路から呼ばないのか:
//   Microsoft.Data.SqlClient はRevitのプロセスへ読み込まれます。
//   Revitが同梱する別バージョンとぶつかる可能性があり、こちらでは確かめられません。
//   利用者がSQL Serverへ切り替えたときにだけ触れる置き方にしておけば、
//   読み込みに失敗してもBUIパネル自体は今までどおり開きます。
//
// 修正時の影響:
//   接続に失敗したらデータ元を変えないでください。
//   変えると「どちらを読んでいるか分からない」状態になります。

using Bui.Common;
using Bui.Configuration;
using Bui.Data;
using Bui.Data.GoldStandard;
using Bui.Data.SqlServer;
using Bui.UI;

namespace Bui.Revit.Data;

/// <summary>設定タブからデータ元を差し替えます。</summary>
internal sealed class RevitDataSourceConnector : IDataSourceConnector
{
    private readonly DataSourceSwitch _switch;
    private readonly ICsvPathSwitchable _priceRepository;
    private readonly ICsvPathSwitchable _assemblyRepository;
    private readonly string _dataRoot;

    public RevitDataSourceConnector(
        DataSourceSwitch dataSourceSwitch,
        ICsvPathSwitchable priceRepository,
        ICsvPathSwitchable assemblyRepository,
        string dataRoot)
    {
        _switch = dataSourceSwitch ?? throw new ArgumentNullException(nameof(dataSourceSwitch));
        _priceRepository = priceRepository ?? throw new ArgumentNullException(nameof(priceRepository));
        _assemblyRepository = assemblyRepository ?? throw new ArgumentNullException(nameof(assemblyRepository));
        _dataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
    }

    public bool SupportsWrite => _switch.SupportsWrite;

    /// <summary>
    /// 接続して件数を数えるだけです。データ元は変えません。
    /// TryRead自体は接続を開き11テーブルを読む同期処理のため、そのままawaitするとRevitのUIスレッドが
    /// 接続タイムアウトやテーブル読み取りの間ずっと固まります。スレッドプールへ逃がします。
    /// </summary>
    public Task<OperationResult<string>> TestSqlServerAsync(
        string serverName, string databaseName, CancellationToken cancellationToken) =>
        // 確認だけなのでデータ元は変えません。画像フォルダは接続結果に影響しないため、
        // いまの_switchの値をそのまま使えば十分です。
        Task.Run(() => TryRead(serverName, databaseName, _switch.DataFolderPath, out _), cancellationToken);

    /// <summary>
    /// 接続に成功した場合だけデータ元を差し替えます。
    /// TryRead本体はスレッドプールで実行し、_switch.SwitchTo（volatileな参照の差し替えだけ）は
    /// 継続が戻ったスレッドでそのまま行います。
    ///
    /// なぜdataFolderPathを引数で受け取るのか:
    ///   以前は_switch.DataFolderPath（切替前の、つまり古いデータ元が持つフォルダ）を
    ///   そのまま引き継いでいました。設定タブでデータフォルダを新しく入力し、
    ///   保存や再起動を挟まずそのままSQL Serverへ切り替えると、編集欄の新しい値が
    ///   無視されて古いフォルダのまま接続され、製品画像が見つからなくなる不具合が
    ///   実際に発生しました（2026-09-25）。呼出元（設定タブ）が持つ編集欄の値を
    ///   ここで明示的に受け取ることで、切替の瞬間に必ず最新の値を使います。
    /// </summary>
    public async Task<OperationResult<string>> UseSqlServerAsync(
        string serverName, string databaseName, string dataFolderPath, CancellationToken cancellationToken)
    {
        var (result, source) = await Task.Run(() =>
        {
            var opResult = TryRead(serverName, databaseName, dataFolderPath, out var candidate);
            return (opResult, candidate);
        }, cancellationToken).ConfigureAwait(false);
        // 失敗したら差し替えません。中途半端な状態を作らないためです。
        if (result.IsSuccess && source is not null) _switch.SwitchTo(source);
        return result;
    }

    public void UseCsvFolder(string folderPath) => _switch.SwitchTo(new CsvTableSource(folderPath));

    public void UsePriceCsv(string configuredPath) =>
        _priceRepository.SwitchCsvPath(Bui.Configuration.CsvPathResolver.ResolvePricePath(configuredPath, _dataRoot));

    public void UseAssemblyCsv(string configuredPath) =>
        _assemblyRepository.SwitchCsvPath(Bui.Configuration.CsvPathResolver.ResolveAssemblyPath(configuredPath, _dataRoot));

    /// <summary>いま読んでいるデータ元の説明です。設定タブに常に表示します。</summary>
    public string Describe() => _switch.Describe();

    /// <summary>実際に読んでみて、成功なら件数の要約を返します。</summary>
    private OperationResult<string> TryRead(
        string serverName, string databaseName, string dataFolderPath, out SqlServerTableSource? source)
    {
        source = null;
        var connectionString = SqlConnectionStringFactory.Build(serverName, databaseName);
        if (!connectionString.IsSuccess)
        {
            return OperationResult<string>.Failure(
                connectionString.Error!.Code, connectionString.Error.UserMessage, connectionString.Error.TechnicalDetail);
        }

        // 画像はSQL接続中もローカルのファイルとして扱うため、呼出元から渡されたデータフォルダを使います。
        var candidate = new SqlServerTableSource(
            () => SqlClientTableReader.Open(connectionString.Value!, databaseName),
            dataFolderPath, serverName, databaseName);

        var set = candidate.Load();
        if (!set.HasProducts)
        {
            // 5行まで出します。3行では、接続できたがテーブルが揃っていない場合に
            // 一覧の先頭2つが埋めてしまい、肝心の理由が画面から切れていました。
            var reason = set.Problems.Count > 0
                ? string.Join(Environment.NewLine, set.Problems.Take(5))
                : "Products テーブルを読み取れませんでした。";
            return OperationResult<string>.Failure("SQL_CONNECT_FAILED", reason, "HasProducts is false");
        }

        source = candidate;
        var summary =
            $"接続できました。製品 {set.Rows(GoldStandardTables.Products).Count}件 / "
            + $"メーカー {set.Rows(GoldStandardTables.Manufacturers).Count}件 / "
            + $"属性 {set.Rows(GoldStandardTables.ProductSpecs).Count}件";
        // Productsは読めても他のテーブルが一部失敗していることがあります（例: Product_Specsだけ失敗し
        // 「属性 0件」になる）。ここでProblemsを捨てると、成功扱いのまま原因が利用者へ届きません。
        if (set.Problems.Count > 0)
        {
            summary += Environment.NewLine + string.Join(Environment.NewLine, set.Problems.Take(2));
        }

        return OperationResult<string>.Success(summary);
    }
}
