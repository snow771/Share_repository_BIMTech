using Microsoft.Data.SqlClient;

namespace ScaffoldAlert.Core.Data;

/// <summary>現場そのものの情報。</summary>
public class SiteInfo
{
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ManagerName { get; set; }

    /// <summary>ACTIVE / SUSPENDED / CLOSED。</summary>
    public string SiteStatus { get; set; } = string.Empty;
}

/// <summary>現場に紐付いている観測所1つ。</summary>
public class LinkedStation
{
    public string StationId { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string? PrefName { get; set; }

    /// <summary>現場からの距離(km)。登録されていないことがある。</summary>
    public decimal? DistanceKm { get; set; }

    /// <summary>最寄りの観測所かどうか。</summary>
    public bool IsNearest { get; set; }
}

/// <summary>
/// 現場に紐付く観測所の中で、最も大きい実測値。
///
/// ★これは「最寄りの観測所の値」ではない。
///   紐付いた複数の観測所のうち最も大きい値であり、
///   各値は別々の観測所・別々の時刻のものであることがある。
///   画面には必ずその断りを出す。
/// </summary>
public class SiteWeather
{
    public decimal? Precip12h { get; set; }
    public decimal? Precip1h { get; set; }
    public decimal? MaxWindSpeed { get; set; }
    public decimal? MaxGustSpeed { get; set; }
    public DateTime? ObservedAt { get; set; }

    /// <summary>1つでも値があるか。無ければ画面に「データなし」と出す。</summary>
    public bool HasAny => Precip12h is not null || Precip1h is not null
                          || MaxWindSpeed is not null
                          || MaxGustSpeed is not null;
}

/// <summary>この現場の市区町村で観測された震度1件。</summary>
public class SiteQuake
{
    public DateTime OccurredAt { get; set; }
    public string? Hypocenter { get; set; }
    public string? Intensity { get; set; }
}

/// <summary>1観測所の、ある時刻の観測値。</summary>
public class StationTrendRow
{
    public DateTime ObservedAt { get; set; }
    public decimal? Precip12h { get; set; }
    public decimal? Precip1h { get; set; }
    public decimal? MaxWindSpeed { get; set; }
    public decimal? MaxGustSpeed { get; set; }
    public decimal? Snow24h { get; set; }
}

/// <summary>
/// 1つの現場の詳細をデータベースから読む係。
///
/// 選んだ1現場だけを問い合わせるので軽い。
/// ダッシュボードを開くたびに553現場分を読むのは無駄なので、
/// 詳細は押されたときに読みに行く。
/// </summary>
public class SiteDetailRepository
{
    private readonly SqlConnectionFactory _factory;

    public SiteDetailRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>現場そのものの情報を読む。見つからなければ空。</summary>
    public SiteInfo? LoadInfo(int siteId)
    {
        const string sql = @"
            SELECT SiteId, SiteName, ISNULL(Department, N'未設定') AS Department,
                   Address, ManagerName, SiteStatus
            FROM dbo.M_Site
            WHERE SiteId = @siteId AND IsDeleted = 0;";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@siteId", siteId);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new SiteInfo
        {
            SiteId = reader.GetInt32(0),
            SiteName = reader.GetString(1),
            Department = reader.GetString(2),
            Address = reader.IsDBNull(3) ? null : reader.GetString(3),
            ManagerName = reader.IsDBNull(4) ? null : reader.GetString(4),
            SiteStatus = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
        };
    }

    /// <summary>紐付いている観測所を、近い順に読む。</summary>
    public List<LinkedStation> LoadStations(int siteId)
    {
        const string sql = @"
            SELECT ss.StationId, st.StationName, st.PrefName,
                   ss.DistanceKm, ss.IsNearest
            FROM dbo.T_SiteStation AS ss
            INNER JOIN dbo.M_AmedasStation AS st
                    ON ss.StationId = st.StationId
            WHERE ss.SiteId = @siteId
            ORDER BY ss.DistanceKm;";

        var list = new List<LinkedStation>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@siteId", siteId);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new LinkedStation
            {
                StationId = reader.GetString(0).Trim(),
                StationName = reader.GetString(1),
                PrefName = reader.IsDBNull(2) ? null : reader.GetString(2),
                DistanceKm = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                IsNearest = !reader.IsDBNull(4) && reader.GetBoolean(4),
            });
        }

        return list;
    }

    /// <summary>
    /// 直近3時間の実測値のうち、最も大きいものを読む。
    ///
    /// 3時間なのは「いまどうか」を見るため。
    /// 判定に使う12時間積算雨量は、この幅でも値そのものは12時間分が入る。
    /// </summary>
    public SiteWeather LoadWeather(int siteId)
    {
        const string sql = @"
            SELECT MAX(o.Precip12h), MAX(o.Precip1h),
                   MAX(o.MaxWindSpeed), MAX(o.MaxGustSpeed),
                   MAX(o.ObservedAt)
            FROM dbo.T_SiteStation AS ss
            INNER JOIN dbo.T_Observation AS o ON ss.StationId = o.StationId
            WHERE ss.SiteId = @siteId
              AND o.ObservedAt >= DATEADD(HOUR, -3, SYSDATETIME());";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@siteId", siteId);
        using var reader = command.ExecuteReader();

        var weather = new SiteWeather();

        if (reader.Read())
        {
            weather.Precip12h = reader.IsDBNull(0) ? null : reader.GetDecimal(0);
            weather.Precip1h = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
            weather.MaxWindSpeed = reader.IsDBNull(2)
                ? null : reader.GetDecimal(2);
            weather.MaxGustSpeed = reader.IsDBNull(3)
                ? null : reader.GetDecimal(3);
            weather.ObservedAt = reader.IsDBNull(4)
                ? null : reader.GetDateTime(4);
        }

        return weather;
    }

    /// <summary>
    /// この現場の市区町村で観測された震度を、直近7日分読む。
    ///
    /// 震度は市区町村ごとに配信される。現場の市区町村コード(5桁)と、
    /// 震度側の7桁コードの先頭5桁を突き合わせる。
    /// </summary>
    public List<SiteQuake> LoadQuakes(int siteId)
    {
        const string sql = @"
            SELECT TOP 5 e.OccurredAt, e.Hypocenter, i.Intensity
            FROM dbo.T_SiteArea AS sa
            INNER JOIN dbo.T_EarthquakeIntensity AS i
                    ON sa.AreaCode = LEFT(i.AreaCode, 5)
            INNER JOIN dbo.T_Earthquake AS e
                    ON i.EarthquakeId = e.EarthquakeId
            WHERE sa.SiteId = @siteId
              AND e.OccurredAt >= DATEADD(DAY, -7, SYSDATETIME())
            ORDER BY e.OccurredAt DESC;";

        var list = new List<SiteQuake>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@siteId", siteId);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new SiteQuake
            {
                OccurredAt = reader.GetDateTime(0),
                Hypocenter = reader.IsDBNull(1) ? null : reader.GetString(1),
                Intensity = reader.IsDBNull(2) ? null : reader.GetString(2),
            });
        }

        return list;
    }

    /// <summary>
    /// 1つの観測所の、直近24時間の観測値を読む。
    ///
    /// 判定に使うのは直近3時間だが、前日からの降り方が分かる方が
    /// 現場への説明に使えるため24時間分を出す。
    /// バッチは1時間毎に動くので、最大24行になる。
    /// </summary>
    public List<StationTrendRow> LoadTrend(string stationId)
    {
        const string sql = @"
            SELECT ObservedAt, Precip12h, Precip1h,
                   MaxWindSpeed, MaxGustSpeed, Snow24h
            FROM dbo.T_Observation
            WHERE StationId = @stationId
              AND ObservedAt >= DATEADD(HOUR, -24, SYSDATETIME())
            ORDER BY ObservedAt DESC;";

        var list = new List<StationTrendRow>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@stationId", stationId);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new StationTrendRow
            {
                ObservedAt = reader.GetDateTime(0),
                Precip12h = reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                Precip1h = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                MaxWindSpeed = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                MaxGustSpeed = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                Snow24h = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            });
        }

        return list;
    }
}
