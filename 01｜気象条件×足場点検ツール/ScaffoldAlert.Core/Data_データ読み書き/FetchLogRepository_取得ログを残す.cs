using Microsoft.Data.SqlClient;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// 取得ログを残す係。
///
/// 気象庁側の障害やネットワーク断で取得できなかったことを、
/// 後から特定できるようにするためのもの。
/// これが無いと「あの日だけデータが無い」理由が分からなくなる。
/// </summary>
public class FetchLogRepository
{
    private readonly SqlConnectionFactory _factory;

    public FetchLogRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>1回の取得の結果を記録する。</summary>
    /// <param name="dataType">PREALL / WIND / GUST / SNOW / QUAKE など。</param>
    public void Write(string dataType, string? url, bool succeeded,
                      int? count = null, string? error = null)
    {
        const string sql = @"
            INSERT INTO dbo.T_FetchLog
                (DataType, TargetUrl, FinishedAt,
                 IsSuccess, RecordCount, ErrorMessage)
            VALUES (@dataType, @url, SYSDATETIME(),
                    @success, @count, @error);";

        try
        {
            using var connection = _factory.CreateOpen();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@dataType", dataType);
            command.Parameters.AddWithValue("@url",
                (object?)url ?? DBNull.Value);
            command.Parameters.AddWithValue("@success", succeeded ? 1 : 0);
            command.Parameters.AddWithValue("@count",
                (object?)count ?? DBNull.Value);
            command.Parameters.AddWithValue("@error",
                error is null ? DBNull.Value
                              : Cut(error));

            command.ExecuteNonQuery();
        }
        catch
        {
            // 記録そのものに失敗しても、呼び出し元は止めない。
            // ここで落とすと、ログを残せないというだけの理由で
            // 取得や判定が丸ごと止まってしまう。
        }
    }

    /// <summary>長すぎる理由は切り詰める。列の幅に収めるため。</summary>
    private static object Cut(string text)
        => text.Length > 1000 ? text.Substring(0, 1000) : text;
}
