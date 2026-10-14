using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Forecast;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// 気象予測を保存する係。
///
/// 予測は発表のたびに置き換わるため、上書きで保存する。
/// 過ぎた予測は使い道が無く、残しておくと画面で新しいものと混ざる。
/// 実測の観測値と違い、証跡としての価値も無いので消す。
/// </summary>
public class ForecastSaveRepository
{
    /// <summary>
    /// 過ぎた予測を残しておく時間。7日ぶん。
    /// </summary>
    public const int KeepHours = 24 * 7;

    private readonly SqlConnectionFactory _factory;

    public ForecastSaveRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 現場と予報区域の対応を保存する。
    ///
    /// 現場が増えたり住所が変わったりしても、次の実行で追従する。
    /// </summary>
    public int SaveSiteAreas(List<SiteAreaLink> links)
    {
        if (links.Count == 0)
        {
            return 0;
        }

        const string sql = @"
            MERGE dbo.T_SiteForecastArea AS tgt
            USING (SELECT @siteId AS SiteId) AS src
            ON tgt.SiteId = src.SiteId
            WHEN MATCHED THEN UPDATE SET
                AreaCode = @areaCode, AreaName = @areaName,
                OfficeCode = @officeCode, OfficeName = @officeName,
                UpdatedAt = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (SiteId, AreaCode, AreaName, OfficeCode, OfficeName)
                VALUES (@siteId, @areaCode, @areaName,
                        @officeCode, @officeName);";

        using var connection = _factory.CreateOpen();
        using var tran = connection.BeginTransaction();

        try
        {
            int saved = 0;

            foreach (var link in links)
            {
                using var command = new SqlCommand(sql, connection, tran);
                command.Parameters.AddWithValue("@siteId", link.SiteId);
                command.Parameters.AddWithValue("@areaCode", link.AreaCode);
                command.Parameters.AddWithValue("@areaName",
                    (object?)link.AreaName ?? DBNull.Value);
                command.Parameters.AddWithValue("@officeCode", link.OfficeCode);
                command.Parameters.AddWithValue("@officeName",
                    (object?)link.OfficeName ?? DBNull.Value);

                command.ExecuteNonQuery();
                saved++;
            }

            tran.Commit();
            return saved;
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 予測を保存する。
    ///
    /// 同じ区域・同じ対象日時・同じ項目は上書きする。
    /// 気象庁は1日に何度も発表し直すため、上書きにしないと
    /// 古い予測が残って画面に混ざる。
    /// </summary>
    public int SaveForecasts(List<ForecastRow> rows)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        const string sql = @"
            MERGE dbo.T_Forecast AS tgt
            USING (SELECT @areaCode AS AreaCode,
                          @targetAt AS TargetAt,
                          @itemType AS ItemType) AS src
            ON tgt.AreaCode = src.AreaCode
               AND tgt.TargetAt = src.TargetAt
               AND tgt.ItemType = src.ItemType
            WHEN MATCHED THEN UPDATE SET
                AreaName = @areaName, Value = @value,
                ValueText = @valueText, ReportedAt = @reportedAt,
                FetchedAt = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (AreaCode, AreaName, TargetAt, ItemType,
                 Value, ValueText, ReportedAt)
                VALUES (@areaCode, @areaName, @targetAt, @itemType,
                        @value, @valueText, @reportedAt);";

        using var connection = _factory.CreateOpen();
        using var tran = connection.BeginTransaction();

        try
        {
            int saved = 0;

            foreach (var r in rows)
            {
                using var command = new SqlCommand(sql, connection, tran);
                command.Parameters.AddWithValue("@areaCode", r.AreaCode);
                command.Parameters.AddWithValue("@areaName",
                    (object?)r.AreaName ?? DBNull.Value);
                command.Parameters.AddWithValue("@targetAt", r.TargetAt);
                command.Parameters.AddWithValue("@itemType", r.ItemType);
                command.Parameters.AddWithValue("@value",
                    (object?)r.Value ?? DBNull.Value);
                command.Parameters.AddWithValue("@valueText",
                    (object?)r.ValueText ?? DBNull.Value);
                command.Parameters.AddWithValue("@reportedAt",
                    (object?)r.ReportedAt ?? DBNull.Value);

                command.ExecuteNonQuery();
                saved++;
            }

            tran.Commit();
            return saved;
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }

    /// <summary>過ぎた予測を消す。</summary>
    public int Cleanup()
    {
        const string sql = @"
            DELETE FROM dbo.T_Forecast
            WHERE TargetAt < DATEADD(HOUR, @hours, SYSDATETIME());";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@hours", -KeepHours);

        return command.ExecuteNonQuery();
    }
}
