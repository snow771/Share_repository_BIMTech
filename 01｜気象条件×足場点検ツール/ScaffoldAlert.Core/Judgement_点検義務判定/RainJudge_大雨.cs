using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Core.Judgement;

/// <summary>
/// 大雨の判定：12時間積算雨量が基準値以上か。
///
/// 労働安全衛生規則 第567条第2項の「大雨」に当たるかを見る。
/// 通達で「1回の降雨量50mm以上」と定義されており、
/// 気象庁が配信している12時間積算値をその基準として用いる。
///
/// 紐付いた観測所のうち最も大きい値を採る。
/// 最寄り1地点だけを見ると「現場では激しく降っていたが、観測所では
/// 降っていなかった」という取りこぼしが起こる。臨検対応では余分に
/// 点検するのは無害だが、見逃しは致命的なため。
/// </summary>
public class RainJudge
{
    private readonly SqlConnectionFactory _factory;
    private readonly AlertRepository _alerts;

    public RainJudge(SqlConnectionFactory factory, AlertRepository alerts)
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

        // 直近3時間の観測値だけを見る。
        // 現場ごとに、紐付いた観測所の中で雨量が最も多い1行を選ぶ。
        const string sql = @"
            SELECT s.SiteId, s.SiteName, x.StationId, st.StationName,
                   x.Precip12h, x.ObservedAt
            FROM dbo.M_Site AS s
            INNER JOIN (
                SELECT ss.SiteId, o.StationId, o.Precip12h, o.ObservedAt,
                       ROW_NUMBER() OVER (
                           PARTITION BY ss.SiteId
                           ORDER BY o.Precip12h DESC, o.ObservedAt DESC
                       ) AS rn
                FROM dbo.T_SiteStation AS ss
                INNER JOIN dbo.T_Observation AS o ON ss.StationId = o.StationId
                WHERE o.ObservedAt >= DATEADD(HOUR, -3, SYSDATETIME())
                  AND o.Precip12h IS NOT NULL
            ) AS x ON s.SiteId = x.SiteId AND x.rn = 1
            INNER JOIN dbo.M_AmedasStation AS st ON x.StationId = st.StationId
            WHERE s.IsDeleted = 0
              AND s.HasScaffold = 1
              AND s.SiteStatus IN (N'ACTIVE', N'SUSPENDED')
              AND x.Precip12h >= @limit;";

        var rows = new List<(int SiteId, string SiteName, string StationId,
                             string StationName, decimal Value, DateTime At)>();

        using (var connection = _factory.CreateOpen())
        using (var command = new SqlCommand(sql, connection))
        {
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
            // 大雨は「同じ現場・同じ事象で12時間以内なら再発報しない」。
            // 雨が降り続いている間ずっとアラートが出るのを防ぐ。
            if (_alerts.HasRecentAlert(r.SiteId, "RAIN", r.At,
                                       JudgeSettings.SuppressHours))
            {
                continue;
            }

            results.Add(new AlertCandidate
            {
                SiteId = r.SiteId,
                SiteName = r.SiteName,
                EventType = "RAIN",
                OccurredAt = r.At,
                TriggerValue = r.Value,
                StationId = r.StationId,
                StationName = r.StationName,
                LegalBasis = threshold.LegalBasis,
                Detail = $"12時間積算雨量 {r.Value:F1}mm (基準 {limit:F0}mm)",
            });
        }

        return results;
    }
}
