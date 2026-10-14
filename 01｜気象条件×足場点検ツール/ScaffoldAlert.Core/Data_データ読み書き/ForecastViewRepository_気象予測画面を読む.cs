using Microsoft.Data.SqlClient;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// 現場に結び付いた予測1行。
/// 予測は区域の単位で発表されるので、同じ区域の現場は同じ値になる。
/// </summary>
public class SiteForecast
{
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;

    /// <summary>府県名を前に付けた区域名 (例 東京都 東京地方)。</summary>
    public string AreaLabel { get; set; } = string.Empty;

    public string AreaCode { get; set; } = string.Empty;

    /// <summary>項目 (最大風速 / ２４時間最大雨量 / 風の予報 など)。</summary>
    public string ItemType { get; set; } = string.Empty;

    public DateTime TargetAt { get; set; }

    /// <summary>数値。数値でない予測 (「高」など) のときは空。</summary>
    public decimal? Value { get; set; }

    /// <summary>階級 (「高」「中」) や単位 (「%」)。</summary>
    public string? ValueText { get; set; }

    public DateTime? ReportedAt { get; set; }
}

/// <summary>部署ごとの、予測の区域との紐付け状況。</summary>
public record ForecastCoverage(string Department, int Total, int Unlinked);

/// <summary>
/// 気象予測の地図に出す現場1つ。
///
/// どの区域に属しているかを持たせている。
/// 区域の警戒度と突き合わせて、危ない現場を目立たせるため。
/// </summary>
public class ForecastSiteMarker
{
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ManagerName { get; set; }

    /// <summary>一次細分区域コード。</summary>
    public string AreaCode { get; set; } = string.Empty;

    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}

/// <summary>
/// 気象予測の画面が使うものをデータベースから読む係。
/// </summary>
public class ForecastViewRepository
{
    private readonly SqlConnectionFactory _factory;

    public ForecastViewRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 現場に結び付いた予測を読む。
    ///
    /// 足場のある現場で、竣工していないものだけを対象にする。
    /// 6時間前より後の予測を読むのは、いま進行中の時間帯を
    /// 落とさないため (予測は6時間刻みで発表される)。
    /// </summary>
    public List<SiteForecast> LoadForecasts()
    {
        const string sql = @"
            SELECT s.SiteId, s.SiteName,
                   ISNULL(s.Department, N'未設定') AS Department,
                   -- 「北西部」「南部」だけでは何県か分からないという声が
                   -- あったため、府県名を前に付けて表示する。
                   ISNULL(f.OfficeName + N' ', N'') + f.AreaName AS AreaLabel,
                   f.AreaCode,
                   fc.ItemType, fc.TargetAt, fc.Value, fc.ValueText,
                   fc.ReportedAt
            FROM dbo.T_Forecast AS fc
            INNER JOIN dbo.T_SiteForecastArea AS f
                    ON fc.AreaCode = f.AreaCode
            INNER JOIN dbo.M_Site AS s ON f.SiteId = s.SiteId
            WHERE s.IsDeleted = 0
              AND s.HasScaffold = 1
              AND s.SiteStatus IN (N'ACTIVE', N'SUSPENDED')
              AND fc.TargetAt >= DATEADD(HOUR, -6, SYSDATETIME())
            ORDER BY fc.TargetAt, s.SiteName;";

        var list = new List<SiteForecast>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 120;
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new SiteForecast
            {
                SiteId = reader.GetInt32(0),
                SiteName = reader.GetString(1),
                Department = reader.GetString(2),
                AreaLabel = reader.IsDBNull(3)
                    ? string.Empty : reader.GetString(3),
                AreaCode = reader.IsDBNull(4)
                    ? string.Empty : reader.GetString(4).Trim(),
                ItemType = reader.GetString(5),
                TargetAt = reader.GetDateTime(6),
                Value = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                ValueText = reader.IsDBNull(8) ? null : reader.GetString(8),
                ReportedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            });
        }

        return list;
    }

    /// <summary>
    /// 気象予測の地図に出す現場を読む。
    ///
    /// 予測の区域に紐付いていて、緯度経度がある現場だけ。
    /// 紐付いていない現場は、どの区域の予測を見ればよいか
    /// 決められないので地図にも出さない。
    /// (その件数は画面の上のほうに別に出している)
    /// </summary>
    public List<ForecastSiteMarker> LoadSiteMarkers()
    {
        const string sql = @"
            SELECT s.SiteId, s.SiteName,
                   ISNULL(s.Department, N'未設定') AS Department,
                   s.Address, s.ManagerName,
                   f.AreaCode, s.Latitude, s.Longitude
            FROM dbo.M_Site AS s
            INNER JOIN dbo.T_SiteForecastArea AS f ON s.SiteId = f.SiteId
            WHERE s.IsDeleted = 0
              AND s.HasScaffold = 1
              AND s.SiteStatus IN (N'ACTIVE', N'SUSPENDED')
              AND s.Latitude IS NOT NULL
              AND s.Longitude IS NOT NULL
            ORDER BY s.SiteId;";

        var list = new List<ForecastSiteMarker>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new ForecastSiteMarker
            {
                SiteId = reader.GetInt32(0),
                SiteName = reader.GetString(1),
                Department = reader.GetString(2),
                Address = reader.IsDBNull(3) ? null : reader.GetString(3),
                ManagerName = reader.IsDBNull(4) ? null : reader.GetString(4),
                AreaCode = reader.IsDBNull(5)
                    ? string.Empty : reader.GetString(5).Trim(),
                Latitude = reader.GetDecimal(6),
                Longitude = reader.GetDecimal(7),
            });
        }

        return list;
    }

    /// <summary>
    /// 1つの現場ぶんの予測だけを読む。
    ///
    /// 一覧は現場ごとに1行へまとめているため、そこからは
    /// 「いつ高くなって、いつ収まるのか」が読み取れない。
    /// 対策会議では時間の並びが判断材料になるので、
    /// 行を押されたときにその現場のぶんだけ読みに行く。
    /// </summary>
    public List<SiteForecast> LoadForSite(int siteId)
    {
        const string sql = @"
            SELECT s.SiteId, s.SiteName,
                   ISNULL(s.Department, N'未設定') AS Department,
                   ISNULL(f.OfficeName + N' ', N'') + f.AreaName AS AreaLabel,
                   f.AreaCode,
                   fc.ItemType, fc.TargetAt, fc.Value, fc.ValueText,
                   fc.ReportedAt
            FROM dbo.T_Forecast AS fc
            INNER JOIN dbo.T_SiteForecastArea AS f
                    ON fc.AreaCode = f.AreaCode
            INNER JOIN dbo.M_Site AS s ON f.SiteId = s.SiteId
            WHERE s.SiteId = @siteId
              AND fc.TargetAt >= DATEADD(HOUR, -6, SYSDATETIME())
            ORDER BY fc.TargetAt;";

        var list = new List<SiteForecast>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@siteId", siteId);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new SiteForecast
            {
                SiteId = reader.GetInt32(0),
                SiteName = reader.GetString(1),
                Department = reader.GetString(2),
                AreaLabel = reader.IsDBNull(3)
                    ? string.Empty : reader.GetString(3),
                AreaCode = reader.IsDBNull(4)
                    ? string.Empty : reader.GetString(4).Trim(),
                ItemType = reader.GetString(5),
                TargetAt = reader.GetDateTime(6),
                Value = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                ValueText = reader.IsDBNull(8) ? null : reader.GetString(8),
                ReportedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            });
        }

        return list;
    }

    /// <summary>
    /// 部署ごとに、予測の区域に紐付いていない現場の数を読む。
    ///
    /// 予測は「区域が割り出せた現場」にしか出ない。
    /// 黙って抜けると「あの部署の現場が出てこない」ことに気づけないため、
    /// 件数を画面に示せるようにする。
    /// </summary>
    public List<ForecastCoverage> LoadCoverage()
    {
        const string sql = @"
            SELECT ISNULL(s.Department, N'未設定') AS Department,
                   COUNT(*) AS Total,
                   SUM(CASE WHEN f.SiteId IS NULL THEN 1 ELSE 0 END) AS Unlinked
            FROM dbo.M_Site AS s
            LEFT JOIN dbo.T_SiteForecastArea AS f ON s.SiteId = f.SiteId
            WHERE s.IsDeleted = 0
              AND s.HasScaffold = 1
              AND s.SiteStatus IN (N'ACTIVE', N'SUSPENDED')
            GROUP BY ISNULL(s.Department, N'未設定')
            ORDER BY SUM(CASE WHEN f.SiteId IS NULL THEN 1 ELSE 0 END) DESC;";

        var list = new List<ForecastCoverage>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new ForecastCoverage(
                reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return list;
    }
}
