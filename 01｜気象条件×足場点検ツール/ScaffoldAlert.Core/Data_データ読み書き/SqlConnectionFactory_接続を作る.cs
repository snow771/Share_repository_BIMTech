using Microsoft.Data.SqlClient;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// データベースへの接続を作る係。
///
/// 接続は「使うときに開いて、終わったら閉じる」のが基本。
/// ここでは開いた状態の接続を返し、呼び出す側が using で閉じる。
///
/// Python版は接続を1つ作って使い回していたが、
/// 「他のコマンドの結果のために、接続がビジー状態」という不具合の原因になった。
/// Webアプリは複数人が同時に使うため、都度作る方式にする。
/// (SQL Server 側で接続を再利用する仕組みが働くので、遅くはならない)
/// </summary>
public class SqlConnectionFactory
{
    private readonly DatabaseSettings _settings;

    public SqlConnectionFactory(DatabaseSettings settings)
    {
        _settings = settings;
    }

    /// <summary>開いた状態の接続を返す。</summary>
    public SqlConnection CreateOpen()
    {
        var connection = new SqlConnection(_settings.ConnectionString);
        connection.Open();
        return connection;
    }
}
