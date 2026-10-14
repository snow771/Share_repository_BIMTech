using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Core.Judgement;

/// <summary>
/// 風の判定の共通部分。強風 (10分間平均) と暴風 (瞬間) で使う。
///
/// 風は2つの基準で見る。
///   強風 … 10分間平均風速 10m/s 以上 (MaxWindSpeed)
///   暴風 … 最大瞬間風速   30m/s 以上 (MaxGustSpeed)
/// どちらかに当たれば点検が必要。どちらに当たったかを記録に残す必要が
/// あるため、別の事象として扱い、あえてまとめていない。
///
/// 値の性質は両方とも「その日の最大値」なので、判定の考え方は同じ。
/// 直近24時間をさかのぼり、同じ現場・同じ日で1件にする。
///
/// 値が「その日の最大値」である以上、点検義務も日ごとに発生する。
/// そのため下のSQLは現場ごとに1行ではなく、現場×日ごとに1行を返す。
/// 現場ごとに1行しか返さないと、判定窓の24時間に前日の強風が残っている
/// 間、当日の強風が一度も評価されないまま抑制され、朝の強風に
/// 作業前へ気づけなくなる。
/// </summary>
public abstract class WindJudgeBase
{
    private readonly SqlConnectionFactory _factory;
    private readonly AlertRepository _alerts;

    protected WindJudgeBase(SqlConnectionFactory factory, AlertRepository alerts)
    {
        _factory = factory;
        _alerts = alerts;
    }

    /// <summary>事象の種類 (WIND または GUST)。</summary>
    protected abstract string EventType { get; }

    /// <summary>速さが入っている列名 (MaxWindSpeed または MaxGustSpeed)。</summary>
    protected abstract string SpeedColumn { get; }

    /// <summary>起時 (実際に吹いた時刻) が入っている列名。</summary>
    protected abstract string TimeColumn { get; }

    /// <summary>画面に出す説明文を作る。</summary>
    protected abstract string BuildDetail(decimal value, decimal limit);

    public List<AlertCandidate> Judge(Threshold threshold)
    {
        var results = new List<AlertCandidate>();
        if (threshold.Value is null)
        {
            return results;
        }

        decimal limit = threshold.Value.Value;

        // 列名だけが違うので、組み立てて使う。
        // 列名はこのクラスの中で決めた固定の文字列で、外から入ってこない。
        string sql = $@"
            SELECT s.SiteId, s.SiteName, x.StationId, st.StationName,
                   x.Speed, x.At
            FROM dbo.M_Site AS s
            INNER JOIN (
                SELECT ss.SiteId, o.StationId,
                       o.{SpeedColumn} AS Speed,
                       COALESCE(o.{TimeColumn}, o.ObservedAt) AS At,
                       ROW_NUMBER() OVER (
                           PARTITION BY ss.SiteId,
                                        CAST(COALESCE(o.{TimeColumn}, o.ObservedAt) AS DATE)
                           ORDER BY o.{SpeedColumn} DESC, o.ObservedAt DESC, o.StationId
                       ) AS rn
                FROM dbo.T_SiteStation AS ss
                INNER JOIN dbo.T_Observation AS o ON ss.StationId = o.StationId
                WHERE o.ObservedAt >= DATEADD(HOUR, @window, SYSDATETIME())
                  AND o.{SpeedColumn} IS NOT NULL
            ) AS x ON s.SiteId = x.SiteId AND x.rn = 1
            INNER JOIN dbo.M_AmedasStation AS st ON x.StationId = st.StationId
            WHERE s.IsDeleted = 0
              AND s.HasScaffold = 1
              AND s.SiteStatus IN (N'ACTIVE', N'SUSPENDED')
              AND x.Speed >= @limit;";

        var rows = new List<(int SiteId, string SiteName, string StationId,
                             string StationName, decimal Value, DateTime At)>();

        using (var connection = _factory.CreateOpen())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@window", -JudgeSettings.WindWindowHours);
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
            // 同じ日に既に同じ事象のアラートがあれば発報しない。
            if (_alerts.HasAlertOnDay(r.SiteId, EventType, r.At))
            {
                continue;
            }

            results.Add(new AlertCandidate
            {
                SiteId = r.SiteId,
                SiteName = r.SiteName,
                EventType = EventType,
                OccurredAt = r.At,
                TriggerValue = r.Value,
                StationId = r.StationId,
                StationName = r.StationName,
                LegalBasis = threshold.LegalBasis,
                Detail = BuildDetail(r.Value, limit),
            });
        }

        return results;
    }
}
