// このファイルの目的:
//   画面から見た「データ元をつなぎ替える操作」を定義します。
//
// なぜインターフェイスにするのか:
//   実際の接続はMicrosoft.Data.SqlClientを使うため、Bui.Addin側にしか置けません。
//   画面側はこの契約だけを知っていれば、SQL Serverの無いこのPCでも単体テストできます。
//
// 修正時の影響:
//   ConnectSqlServerAsyncは、接続に成功した場合だけデータ元を切り替えてください。
//   失敗したのに切り替えると「どちらを読んでいるか分からない」状態になります。

using Bui.Common;

namespace Bui.UI;

/// <summary>データ元をつなぎ替える操作です。</summary>
public interface IDataSourceConnector
{
    /// <summary>
    /// SQL Serverへ接続できるかだけを確かめます。**データ元は切り替えません。**
    /// 成功時の値は画面へそのまま出す件数の要約です。
    /// 確認と切り替えを同じメソッドにすると、「接続を確認」を押しただけで
    /// データ元が変わり、利用者の意図と画面が食い違います。
    /// </summary>
    Task<OperationResult<string>> TestSqlServerAsync(
        string serverName, string databaseName, CancellationToken cancellationToken);

    /// <summary>
    /// SQL Serverへ接続し、成功したらデータ元をそちらへ切り替えます。
    /// 失敗時は理由を返し、データ元は変えません。
    /// dataFolderPathは、切替後も製品画像をローカルファイルとして探すための
    /// フォルダです（設定タブの編集欄の値をそのまま渡してください）。
    /// </summary>
    Task<OperationResult<string>> UseSqlServerAsync(
        string serverName, string databaseName, string dataFolderPath, CancellationToken cancellationToken);

    /// <summary>データ元をCSVフォルダへ戻します。</summary>
    void UseCsvFolder(string folderPath);

    /// <summary>単価CSVの読み込み先を切り替えます。空欄ならBUI管理下の既定ファイルを使います。</summary>
    void UsePriceCsv(string configuredPath);

    /// <summary>構成CSVの読み込み先を切り替えます。空欄ならBUI管理下の既定ファイルを使います。</summary>
    void UseAssemblyCsv(string configuredPath);

    /// <summary>いまのデータ元へ書き込めるかどうかです。</summary>
    bool SupportsWrite { get; }

    /// <summary>
    /// いま読んでいるデータ元の説明です。接続文字列は含めません。
    /// 設定タブに常に表示し、「切り替えて読み直す」を押し忘れたまま
    /// ラジオボタンだけ変えた状態に気づけるようにします。
    /// </summary>
    string Describe();
}
