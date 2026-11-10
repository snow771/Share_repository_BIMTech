// このファイルの目的:
//   サーバー名とデータベース名から、SQL Serverの接続文字列を組み立てます。
//
// なぜ利用者に接続文字列を書かせないのか:
//   書式の誤りは「接続できません」としか出ず、会社PCでしか再現できません。
//   入力を2つに絞れば、間違いようがある箇所も2つに絞れます。
//
// 修正時の影響:
//   セミコロンなどを弾いています。通すと、利用者の入力で
//   Integrated Security を上書きできてしまいます。

using Bui.Common;

namespace Bui.Configuration;

/// <summary>Windows認証でSQL Serverへ接続する文字列を組み立てます。</summary>
public static class SqlConnectionStringFactory
{
    /// <summary>接続文字列の値として使えない文字です。</summary>
    private static readonly char[] Forbidden = { ';', '=', '\'', '"', '\r', '\n' };

    public static OperationResult<string> Build(string? serverName, string? databaseName)
    {
        var server = (serverName ?? string.Empty).Trim();
        var database = (databaseName ?? string.Empty).Trim();

        if (server.Length == 0)
        {
            return OperationResult<string>.Failure(
                "SQL_SERVER_NAME_REQUIRED",
                "サーバー名を入力してください。例: localhost\\sqlexpress",
                "server name is empty");
        }

        if (database.Length == 0)
        {
            return OperationResult<string>.Failure(
                "SQL_DATABASE_NAME_REQUIRED",
                "データベース名を入力してください。例: GoldStandardDB",
                "database name is empty");
        }

        if (server.IndexOfAny(Forbidden) >= 0 || database.IndexOfAny(Forbidden) >= 0)
        {
            return OperationResult<string>.Failure(
                "SQL_NAME_HAS_FORBIDDEN_CHARACTER",
                "サーバー名またはデータベース名に使えない文字が含まれています。記号を外してください。",
                "forbidden character in server or database name");
        }

        // Windows認証なので、保存も送信もするパスワードはありません。
        // TrustServerCertificateは、既定の自己署名証明書のままのSQL Server Expressへ
        // 社内で接続するための指定です。
        var connectionString =
            $"Data Source={server};Initial Catalog={database};"
            + "Integrated Security=True;TrustServerCertificate=True;"
            + "Connect Timeout=5;Application Name=BUI";

        return OperationResult<string>.Success(connectionString);
    }
}
