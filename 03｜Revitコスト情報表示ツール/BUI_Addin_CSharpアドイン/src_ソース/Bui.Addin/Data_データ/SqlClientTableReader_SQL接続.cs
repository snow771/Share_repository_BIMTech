// このファイルの目的:
//   Microsoft.Data.SqlClient を使って、1枚の表を読み出します。
//   SQLのパッケージに触れるのはこのファイルだけです。
//
// なぜBui.Addin側に置くのか:
//   Bui.Sharedはパッケージ依存を持たせません。持たせると、
//   SQL Serverの無い環境でも単体テストがパッケージを要求します。
//
// 修正時の影響:
//   このクラスに触れるのは、利用者が設定タブでSQL Serverへ切り替えたときだけです。
//   起動経路から呼ばないでください。呼ぶと、アセンブリの読み込みに失敗したときに
//   BUIパネルごと開かなくなります。

using System.Data;
using Bui.Data;
using Bui.Data.GoldStandard;
using Microsoft.Data.SqlClient;

namespace Bui.Data.SqlServer;

/// <summary>SQL Serverの1接続で、表を順に読み出します。</summary>
internal sealed class SqlClientTableReader : ISqlTableReader
{
    private readonly SqlConnection _connection;
    private readonly string _databaseName;

    private SqlClientTableReader(SqlConnection connection, string databaseName)
    {
        _connection = connection;
        _databaseName = databaseName;
    }

    /// <summary>接続を開きます。失敗したら日本語の理由付きで投げます。</summary>
    public static ISqlTableReader Open(string connectionString, string databaseName)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            connection.Open();
            return new SqlClientTableReader(connection, databaseName);
        }
        catch (SqlException exception)
        {
            connection.Dispose();
            throw new InvalidOperationException(
                SqlErrorMessages.Describe(exception.Number, databaseName, exception.Message), exception);
        }
        catch (Exception exception)
        {
            connection.Dispose();
            // アセンブリの読み込み失敗はここに来ます。落ちる代わりに読める文言にします。
            // TypeInitializationExceptionはexception.Messageだけでは
            // 「型初期化子で例外が発生しました」としか分からず、本当の原因
            // （InnerExceptionの中）が画面に出ません。2026-09-15、この理由不足で
            // 診断が長引いたため、InnerExceptionの連鎖をすべて連結して出します。
            throw new InvalidOperationException(
                "SQL接続に必要な部品を読み込めませんでした。"
                + "配置先にSQL用のDLLが届いているかを配置診断で確認してください。"
                + Environment.NewLine + DescribeExceptionChain(exception), exception);
        }
    }

    /// <summary>例外とすべてのInnerExceptionのMessageを、型名付きでつなげます。</summary>
    private static string DescribeExceptionChain(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add($"[{current.GetType().Name}] {current.Message}");
        }
        return string.Join(Environment.NewLine + " → ", messages);
    }

    public GoldStandardTable? Read(string table)
    {
        // 表名はGoldStandardTables.Allの固定値だけです。外部入力は入りません。
        using var command = new SqlCommand($"SELECT * FROM dbo.[{table}]", _connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 30
        };

        try
        {
            using var reader = command.ExecuteReader();
            var headers = new List<string>(reader.FieldCount);
            for (var index = 0; index < reader.FieldCount; index++) headers.Add(reader.GetName(index));

            var rows = new List<Dictionary<string, string>>();
            while (reader.Read())
            {
                // SQL Serverの既定の照合順序は大文字小文字を区別しません。列を「Product_ID」で
                // 宣言していても、reader.GetName()はそのままの表記（大文字始まり）で返ってきます。
                // Ordinalのままだと下流の検索キー（すべて小文字のproduct_id想定）と一致せず、
                // HasProductsは真・件数も出るのに製品カードが軒並み空欄になる、という
                // 気づきにくい壊れ方をします。ここは必ずOrdinalIgnoreCaseにします。
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    // NULLはCSVの空欄と同じ「空文字」に揃えます。
                    row[headers[index]] = reader.IsDBNull(index)
                        ? string.Empty
                        : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                }

                rows.Add(row);
            }

            return new GoldStandardTable(headers, rows, HasUtf8Bom: false);
        }
        catch (SqlException exception) when (exception.Number is 208)
        {
            // 208 = テーブルが無い。CSVの「ファイルが無い」と同じ扱いにします。
            return null;
        }
    }

    public void Dispose() => _connection.Dispose();
}
