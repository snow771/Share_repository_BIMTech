using Microsoft.Data.SqlClient;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// 観測所をデータベースから読む係。
/// </summary>
public class StationRepository
{
    private readonly SqlConnectionFactory _factory;

    public StationRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 気象データを取りに行く対象の観測所番号を読む。
    ///
    /// 全国約1,300地点すべてではなく、現場に紐付いた観測所だけにする。
    /// 現場が増えれば監視対象も自動的に増える。
    /// 竣工済み(CLOSED)の現場は点検義務が無いので除く。
    /// </summary>
    public HashSet<string> LoadTargetStationIds()
    {
        const string sql = @"
            SELECT DISTINCT ss.StationId
            FROM dbo.T_SiteStation AS ss
            INNER JOIN dbo.M_Site AS s ON ss.SiteId = s.SiteId
            WHERE s.IsDeleted = 0
              AND s.SiteStatus IN (N'ACTIVE', N'SUSPENDED');";

        var result = new HashSet<string>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            result.Add(reader.GetString(0).Trim());
        }

        return result;
    }

    /// <summary>
    /// 観測所番号から観測所名を引く表を読む。
    /// 画面に番号だけを並べても、どこの値なのか分からないため。
    /// </summary>
    public Dictionary<string, string> LoadStationNames()
    {
        const string sql = @"
            SELECT StationId, StationName, PrefName
            FROM dbo.M_AmedasStation;";

        var result = new Dictionary<string, string>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            string id = reader.GetString(0).Trim();
            string name = reader.GetString(1);
            string pref = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

            result[id] = string.IsNullOrEmpty(pref) ? name : $"{pref} {name}";
        }

        return result;
    }
}
