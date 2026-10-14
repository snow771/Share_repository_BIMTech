using Microsoft.Data.SqlClient;

namespace ScaffoldAlert.Core.Data;

/// <summary>地図に出す現場1つ。</summary>
public class SiteMarker
{
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string SiteStatus { get; set; } = string.Empty;
    public string? ManagerName { get; set; }

    /// <summary>この現場が見ている観測所の数。</summary>
    public int StationCount { get; set; }

    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    /// <summary>
    /// 対応状況。「未対応」「指示済」「なし」のいずれか。
    /// データベースから読んだあとに入れる。
    /// </summary>
    public string AlertState { get; set; } = "なし";
}

/// <summary>対応が必要なアラート1件。</summary>
public class DashboardAlert
{
    public int AlertId { get; set; }
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public decimal? TriggerValue { get; set; }
    public string? TriggerText { get; set; }
    public string AlertStatus { get; set; } = string.Empty;
    public int ElapsedHours { get; set; }
    public string? ManagerName { get; set; }
}

/// <summary>
/// ダッシュボードに出すものをデータベースから読む係。
/// </summary>
public class DashboardRepository
{
    private readonly SqlConnectionFactory _factory;

    public DashboardRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 地図に出す現場を読む。
    ///
    /// 緯度経度が入っていない現場は地図に出せないので除く。
    /// 竣工済みの現場も出す。地図の役割は「全体の中から
    /// アラートが出ている現場を見つけること」なので、
    /// ここで絞り込むと目的に反する。
    /// </summary>
    public List<SiteMarker> LoadSites()
    {
        const string sql = @"
            SELECT s.SiteId, s.SiteName,
                   ISNULL(s.Department, N'未設定') AS Department,
                   s.Address, s.SiteStatus, s.ManagerName,
                   COUNT(ss.SiteStationId) AS StationCount,
                   s.Latitude, s.Longitude
            FROM dbo.M_Site AS s
            LEFT JOIN dbo.T_SiteStation AS ss ON s.SiteId = ss.SiteId
            WHERE s.IsDeleted = 0
              AND s.Latitude IS NOT NULL
              AND s.Longitude IS NOT NULL
            GROUP BY s.SiteId, s.SiteName, s.Department, s.Address,
                     s.SiteStatus, s.ManagerName, s.Latitude, s.Longitude
            ORDER BY s.SiteId;";

        var list = new List<SiteMarker>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new SiteMarker
            {
                SiteId = reader.GetInt32(0),
                SiteName = reader.GetString(1),
                Department = reader.GetString(2),
                Address = reader.IsDBNull(3) ? null : reader.GetString(3),
                SiteStatus = reader.IsDBNull(4)
                    ? string.Empty : reader.GetString(4),
                ManagerName = reader.IsDBNull(5) ? null : reader.GetString(5),
                StationCount = reader.GetInt32(6),
                Latitude = reader.GetDecimal(7),
                Longitude = reader.GetDecimal(8),
            });
        }

        return list;
    }

    /// <summary>
    /// 現場ごとの対応状況を読む。
    /// 未対応があれば、指示済より優先して「未対応」とする。
    /// </summary>
    public Dictionary<int, string> LoadAlertStates()
    {
        const string sql = @"
            SELECT SiteId,
                   MIN(CASE WHEN AlertStatus = N'OPEN' THEN 0 ELSE 1 END) AS Priority
            FROM dbo.T_Alert
            WHERE AlertStatus IN (N'OPEN', N'INSTRUCTED')
            GROUP BY SiteId;";

        var result = new Dictionary<int, string>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            result[reader.GetInt32(0)] =
                reader.GetInt32(1) == 0 ? "未対応" : "指示済";
        }

        return result;
    }

    /// <summary>
    /// 対応が必要なアラートを読む。新しいものから順に並べる。
    /// </summary>
    public List<DashboardAlert> LoadPendingAlerts()
    {
        // 部署は V_PendingAlert に入っていないため、現場マスタから取る。
        const string sql = @"
            SELECT v.AlertId, v.SiteId, v.SiteName, v.Address, v.EventType,
                   v.OccurredAt, v.TriggerValue, v.TriggerText, v.AlertStatus,
                   v.ElapsedHours, v.ManagerName,
                   ISNULL(s.Department, N'未設定') AS Department
            FROM dbo.V_PendingAlert AS v
            INNER JOIN dbo.M_Site AS s ON v.SiteId = s.SiteId
            ORDER BY v.OccurredAt DESC;";

        var list = new List<DashboardAlert>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new DashboardAlert
            {
                AlertId = reader.GetInt32(0),
                SiteId = reader.GetInt32(1),
                SiteName = reader.GetString(2),
                Address = reader.IsDBNull(3) ? null : reader.GetString(3),
                EventType = reader.GetString(4),
                OccurredAt = reader.GetDateTime(5),
                TriggerValue = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                TriggerText = reader.IsDBNull(7) ? null : reader.GetString(7),
                AlertStatus = reader.GetString(8),
                ElapsedHours = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                ManagerName = reader.IsDBNull(10) ? null : reader.GetString(10),
                Department = reader.GetString(11),
            });
        }

        return list;
    }

    /// <summary>稼働中の現場の数。</summary>
    public int CountActiveSites()
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM dbo.M_Site
            WHERE IsDeleted = 0 AND SiteStatus = N'ACTIVE';";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
