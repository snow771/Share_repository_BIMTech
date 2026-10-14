using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Core.Judgement;

/// <summary>
/// 大雪の判定：24時間降雪量が基準値 (25cm) 以上か。
///
/// 労働安全衛生規則 第567条第2項の「大雪」に当たるかを見る。
/// 通達で「1回の降雪量25cm以上」と定義されており、
/// 気象庁が配信している24時間降雪量をその基準として用いる。
///
/// 降雪量は「その時間に新たに降り積もった量」で、
/// 積雪深 (今どれだけ積もっているか) とは別物。
/// 通達が言う「降雪量」に合わせている。
/// </summary>
public class SnowJudge
{
    private readonly SqlConnectionFactory _factory;
    private readonly AlertRepository _alerts;

    public SnowJudge(SqlConnectionFactory factory, AlertRepository alerts)
    {
        _factory = factory;
        _alerts = alerts;
    }

    public List<AlertCandidate> Judge(Threshold threshold)
    {
        var results = new List<AlertCandidate>();
        if (threshold.Value is null)
        {
            return results;
        }

        decimal limit = threshold.Value.Value;

        // 値は積算なので雨と同じ考え方にし、同じ現場・同じ日で1件とする。
        // 降り続いている間ずっとアラートが出るのを防ぐため。
        const string sql = @"
            SELECT s.SiteId, s.SiteName, x.StationId, st.StationName,
                   x.Snow24h, x.ObservedAt
            FROM dbo.M_Site AS s
            INNER JOIN (
                SELECT ss.SiteId, o.StationId, o.Snow24h, o.ObservedAt,
                       ROW_NUMBER() OVER (
                           PARTITION BY ss.SiteId, CAST(o.ObservedAt AS DATE)
                           ORDER BY o.Snow24h DESC, o.ObservedAt DESC, o.StationId
                       ) AS rn
                FROM dbo.T_SiteStation AS ss
                INNER JOIN dbo.T_Observation AS o ON ss.StationId = o.StationId
                WHERE o.ObservedAt >= DATEADD(HOUR, @window, SYSDATETIME())
                  AND o.Snow24h IS NOT NULL
            ) AS x ON s.SiteId = x.SiteId AND x.rn = 1
            INNER JOIN dbo.M_AmedasStation AS st ON x.StationId = st.StationId
            WHERE s.IsDeleted = 0
              AND s.HasScaffold = 1
              AND s.SiteStatus IN (N'ACTIVE', N'SUSPENDED')
              AND x.Snow24h >= @limit;";

        var rows = new List<(int SiteId, string SiteName, string StationId,
                             string StationName, decimal Value, DateTime At)>();

        using (var connection = _factory.CreateOpen())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@window", -JudgeSettings.SnowWindowHours);
            command.Parameters.AddWithValue("@limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetInt32(0), reader.GetString(1),
                          reader.GetString(2), reader.GetString(3),
                          reader.GetDecimal(4), reader.GetDateTime(5)));
            }
        }

        foreach (var r in rows)
        {
            if (_alerts.HasAlertOnDay(r.SiteId, "SNOW", r.At))
            {
                continue;
            }

            results.Add(new AlertCandidate
            {
                SiteId = r.SiteId,
                SiteName = r.SiteName,
                EventType = "SNOW",
                OccurredAt = r.At,
                TriggerValue = r.Value,
                StationId = r.StationId,
                StationName = r.StationName,
                LegalBasis = threshold.LegalBasis,
                Detail = $"24時間降雪量 {r.Value:F1}cm (基準 {limit:F0}cm)",
            });
        }

        return results;
    }
}
