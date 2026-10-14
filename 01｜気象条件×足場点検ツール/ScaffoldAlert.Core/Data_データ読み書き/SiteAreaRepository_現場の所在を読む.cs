using Microsoft.Data.SqlClient;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// 区域を割り出すために必要な、現場の所在。
/// </summary>
public class SiteLocation
{
    public int SiteId { get; set; }

    /// <summary>市区町村コード(5桁)。持っていない現場もある。</summary>
    public string? MunicipalityCode { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    /// <summary>ACTIVE / SUSPENDED / CLOSED。</summary>
    public string SiteStatus { get; set; } = string.Empty;
}

/// <summary>
/// 現場の所在をデータベースから読む係。
/// </summary>
public class SiteAreaRepository
{
    private readonly SqlConnectionFactory _factory;

    public SiteAreaRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 区域を割り出す対象の現場を読む。
    ///
    /// 市区町村コードを持たない現場もいるため、現場マスタを起点にする。
    /// 対応表(T_SiteArea)を起点にすると、コードが無い現場は最初から
    /// 対象外になり、予測に出てこないことに気づけない。
    /// </summary>
    public List<SiteLocation> LoadLocations()
    {
        const string sql = @"
            SELECT s.SiteId, MIN(sa.AreaCode) AS AreaCode,
                   s.Latitude, s.Longitude, s.SiteStatus
            FROM dbo.M_Site AS s
            LEFT JOIN dbo.T_SiteArea AS sa ON s.SiteId = sa.SiteId
            WHERE s.IsDeleted = 0
            GROUP BY s.SiteId, s.Latitude, s.Longitude, s.SiteStatus;";

        var result = new List<SiteLocation>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            result.Add(new SiteLocation
            {
                SiteId = reader.GetInt32(0),
                MunicipalityCode = reader.IsDBNull(1)
                    ? null : reader.GetString(1).Trim(),
                Latitude = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                Longitude = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                SiteStatus = reader.IsDBNull(4)
                    ? string.Empty : reader.GetString(4).Trim(),
            });
        }

        return result;
    }
}
