using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Core.Judgement;

/// <summary>
/// 地震の判定：現場がある市区町村の震度が基準 (震度4) 以上か。
///
/// 労働安全衛生規則 第567条第2項の「中震以上の地震」に当たるかを見る。
/// 通達で「震度階級4以上」と定義されている。
///
/// 気象庁が配信する震度のコードは7桁で、その先頭5桁が
/// 国土地理院の市区町村コードと一致する。
/// 現場側 (T_SiteArea) は市区町村コード5桁を持つため、
/// 先頭5桁どうしで突き合わせる。
///
/// 薩摩川内市や佐世保市のように、1つの市区町村がさらに細かい区域に
/// 分かれて震度が発表されることがある。その場合は1現場・1地震に対して
/// 複数行が返るため、最も大きい震度を採る。
/// </summary>
public class EarthquakeJudge
{
    private readonly SqlConnectionFactory _factory;
    private readonly AlertRepository _alerts;

    public EarthquakeJudge(SqlConnectionFactory factory, AlertRepository alerts)
    {
        _factory = factory;
        _alerts = alerts;
    }

    public List<AlertCandidate> Judge(Threshold threshold, List<string> warnings)
    {
        var results = new List<AlertCandidate>();

        // しきい値は文字 ("4") で入っているので、順位に直す。
        string limitText = (threshold.Text ?? "4").Trim();
        int limit = JudgeSettings.IntensityRank.TryGetValue(limitText, out var v)
            ? v : 4;

        const string sql = @"
            SELECT s.SiteId, s.SiteName, e.EarthquakeId, e.OccurredAt,
                   e.Hypocenter, i.Intensity, i.AreaName
            FROM dbo.M_Site AS s
            INNER JOIN dbo.T_SiteArea AS sa            ON s.SiteId = sa.SiteId
            INNER JOIN dbo.T_EarthquakeIntensity AS i  ON sa.AreaCode = LEFT(i.AreaCode, 5)
            INNER JOIN dbo.T_Earthquake AS e           ON i.EarthquakeId = e.EarthquakeId
            WHERE s.IsDeleted = 0
              AND s.HasScaffold = 1
              AND s.SiteStatus IN (N'ACTIVE', N'SUSPENDED')
              AND e.OccurredAt >= DATEADD(HOUR, @window, SYSDATETIME())
              AND e.OccurredAt >= @startAt
            ORDER BY s.SiteId, e.EarthquakeId, i.AreaCode;";

        // 現場と地震の組ごとに、最も大きい震度を残す。
        var best = new Dictionary<(int SiteId, int EarthquakeId), Found>();
        var unknownLevels = new SortedSet<string>();

        using (var connection = _factory.CreateOpen())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue(
                "@window", -JudgeSettings.EarthquakeWindowHours);
            command.Parameters.AddWithValue(
                "@startAt", JudgeSettings.EarthquakeStartAt);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int siteId = reader.GetInt32(0);
                string siteName = reader.GetString(1);
                int eqId = reader.GetInt32(2);
                DateTime occurred = reader.GetDateTime(3);
                string? hypo = reader.IsDBNull(4) ? null : reader.GetString(4);
                string level = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim();
                string? area = reader.IsDBNull(6) ? null : reader.GetString(6);

                // 順位表に無い震度表記は 0 として扱い、判定は変えない
                // (= 発報しない)。ただし「見逃しは致命的」という方針上、
                // 黙って無視すると本来発報すべき地震を静かに取りこぼす
                // 恐れがあるため、集めておいて最後に警告として出す。
                if (!JudgeSettings.IntensityRank.ContainsKey(level))
                {
                    unknownLevels.Add(level);
                }

                int rank = JudgeSettings.IntensityRank.TryGetValue(level, out var r)
                    ? r : 0;

                var key = (siteId, eqId);
                if (!best.TryGetValue(key, out var current) || rank > current.Rank)
                {
                    best[key] = new Found(rank, siteName, occurred, hypo, level, area);
                }
            }
        }

        if (unknownLevels.Count > 0)
        {
            warnings.Add("想定外の震度表記があり、判定できませんでした: "
                         + string.Join(", ", unknownLevels));
        }

        // 突き合わせが1件も返らなかったときの検知。
        // 上の警告は「行が返ってきた」場合しか働かない。
        // コードの体系がずれて1行も返らなくなる不具合は、それだけでは
        // 気づけない。そこで「判定対象の期間に震度4以上の地域が
        // 発表されていたか」を別に数え、発表があったのに現場が1件も
        // 当たらなかったときに警告を出す。判定そのものには影響しない。
        WarnIfNoMatch(limit, best.Count, warnings);

        foreach (var ((siteId, eqId), f) in best)
        {
            if (f.Rank < limit)
            {
                continue;
            }

            // 地震は「いつ起きたか」がはっきりしているので、
            // 時間ではなく地震そのもので重複を見る。
            if (_alerts.HasAlertForEarthquake(siteId, eqId))
            {
                continue;
            }

            results.Add(new AlertCandidate
            {
                SiteId = siteId,
                SiteName = f.SiteName,
                EventType = "EARTHQUAKE",
                OccurredAt = f.OccurredAt,
                TriggerText = f.Level,
                StationName = f.AreaName ?? "",
                EarthquakeId = eqId,
                LegalBasis = threshold.LegalBasis,
                Detail = $"震度 {f.Level} ({f.AreaName ?? "地域不明"}) "
                         + $"震源: {f.Hypocenter ?? "不明"}",
            });
        }

        return results;
    }

    /// <summary>
    /// 震度4以上の発表があったのに、現場が1件も当たらなかった場合に警告する。
    /// 市区町村コードの突き合わせが壊れたときに気づくための仕組み。
    /// </summary>
    private void WarnIfNoMatch(int limit, int matched, List<string> warnings)
    {
        const string sql = @"
            SELECT DISTINCT i.AreaCode, i.Intensity
            FROM dbo.T_EarthquakeIntensity AS i
            INNER JOIN dbo.T_Earthquake AS e ON i.EarthquakeId = e.EarthquakeId
            WHERE e.OccurredAt >= DATEADD(HOUR, @window, SYSDATETIME())
              AND e.OccurredAt >= @startAt;";

        int strong = 0;

        using (var connection = _factory.CreateOpen())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue(
                "@window", -JudgeSettings.EarthquakeWindowHours);
            command.Parameters.AddWithValue(
                "@startAt", JudgeSettings.EarthquakeStartAt);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string level = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                if (JudgeSettings.IntensityRank.TryGetValue(level, out var rank)
                    && rank >= limit)
                {
                    strong++;
                }
            }
        }

        if (strong > 0 && matched == 0)
        {
            warnings.Add(
                $"直近{JudgeSettings.EarthquakeWindowHours}時間に震度4以上の地域が "
                + $"{strong} 件発表されていますが、該当する現場が1件もありません。"
                + "市区町村コードの突き合わせを確認してください。");
        }
    }

    /// <summary>現場と地震の組ごとに残す、最も大きい震度の情報。</summary>
    private record Found(int Rank, string SiteName, DateTime OccurredAt,
                         string? Hypocenter, string Level, string? AreaName);
}
