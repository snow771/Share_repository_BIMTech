using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Judgement;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// 既に出ているアラート (T_Alert) を調べる係。
///
/// 同じ事象を何度も発報しないための確認に使う。
/// この段階では読むだけで、書き込みは行わない。
///
/// ★使っているテーブルと項目
///   dbo.T_Alert
///     SiteId              現場ID
///     EventType           事象の種類
///     OccurredAt          発生日時
///     SourceEarthquakeId  もとになった地震のID
/// </summary>
public class AlertRepository
{
    private readonly SqlConnectionFactory _factory;

    public AlertRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 同じ現場・同じ事象で、指定した時刻から遡って一定時間内に
    /// アラートが既にあるかを調べる。大雨で使う。
    /// </summary>
    public bool HasRecentAlert(int siteId, string eventType,
                               DateTime occurredAt, int suppressHours)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM dbo.T_Alert
            WHERE SiteId = @siteId
              AND EventType = @eventType
              AND OccurredAt >= DATEADD(HOUR, @hours, @occurredAt);";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@siteId", siteId);
        command.Parameters.AddWithValue("@eventType", eventType);
        command.Parameters.AddWithValue("@hours", -suppressHours);
        command.Parameters.AddWithValue("@occurredAt", occurredAt);

        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// 同じ現場・同じ事象・同じ日にアラートが既にあるかを調べる。
    /// 強風・暴風・大雪で使う。
    ///
    /// 気象庁が配信する風速や降雪量は「その日の最大値」で、
    /// 値が更新されるたびに時刻も動く。そのため時刻の一致で見ると
    /// 同じ事象を別物と誤認して何件も発報してしまう。
    /// 日で見れば取り違えない。
    /// </summary>
    public bool HasAlertOnDay(int siteId, string eventType, DateTime occurredAt)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM dbo.T_Alert
            WHERE SiteId = @siteId
              AND EventType = @eventType
              AND CAST(OccurredAt AS DATE) = CAST(@occurredAt AS DATE);";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@siteId", siteId);
        command.Parameters.AddWithValue("@eventType", eventType);
        command.Parameters.AddWithValue("@occurredAt", occurredAt);

        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// いま未対応・指示済のアラートを読む。
    ///
    /// これは「今回の判定で新しく出たもの」ではなく、
    /// 「これまでに記録され、まだ対応が終わっていないもの」。
    /// Python版が実行の最後に表示している一覧と同じ内容。
    ///
    /// 判定の結果と混同しやすいため、画面でも別の見出しで分けて出す。
    ///
    /// ★使っているビュー
    ///   dbo.V_PendingAlert
    ///     未対応(OPEN)と指示済(INSTRUCTED)だけを集めたビュー。
    ///     削除された現場は除かれている。
    /// </summary>
    public List<PendingAlert> LoadPending()
    {
        const string sql = @"
            SELECT SiteName, EventType, OccurredAt, TriggerValue,
                   TriggerText, AlertStatus, ElapsedHours
            FROM dbo.V_PendingAlert
            ORDER BY OccurredAt DESC;";

        var list = new List<PendingAlert>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new PendingAlert(
                SiteName: reader.GetString(0),
                EventType: reader.GetString(1),
                OccurredAt: reader.GetDateTime(2),
                TriggerValue: reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                TriggerText: reader.IsDBNull(4) ? null : reader.GetString(4),
                AlertStatus: reader.GetString(5),
                ElapsedHours: reader.IsDBNull(6) ? 0 : reader.GetInt32(6)));
        }

        return list;
    }

    /// <summary>
    /// 同じ現場・同じ地震で、既にアラートを出していないかを調べる。
    ///
    /// 地震は「いつ起きたか」がはっきりしているので、
    /// 時間ではなく地震そのもので重複を見る。
    /// </summary>
    public bool HasAlertForEarthquake(int siteId, int earthquakeId)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM dbo.T_Alert
            WHERE SiteId = @siteId
              AND EventType = N'EARTHQUAKE'
              AND SourceEarthquakeId = @earthquakeId;";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@siteId", siteId);
        command.Parameters.AddWithValue("@earthquakeId", earthquakeId);

        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// 判定で見つかったアラートを登録する。
    ///
    /// 状況は未対応(OPEN)で登録する。
    /// このあと管理本部が画面から 指示済 → 点検完了 と進めていく。
    ///
    /// 根拠条文をアラート1件ごとに保存しておく。
    /// 臨検で「なぜこの日に点検が必要だったのか」を即答するため。
    /// 後から基準値を変更しても、過去のアラートの根拠は変わらない。
    /// </summary>
    public int InsertAlerts(List<AlertCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return 0;
        }

        const string sql = @"
            INSERT INTO dbo.T_Alert
                (SiteId, EventType, OccurredAt, TriggerValue, TriggerText,
                 SourceStationId, SourceEarthquakeId, LegalBasis, AlertStatus)
            VALUES (@siteId, @eventType, @occurredAt, @value, @text,
                    @stationId, @earthquakeId, @basis, N'OPEN');";

        using var connection = _factory.CreateOpen();
        using var tran = connection.BeginTransaction();

        try
        {
            int saved = 0;

            foreach (var c in candidates)
            {
                using var command = new SqlCommand(sql, connection, tran);
                command.Parameters.AddWithValue("@siteId", c.SiteId);
                command.Parameters.AddWithValue("@eventType", c.EventType);
                command.Parameters.AddWithValue("@occurredAt", c.OccurredAt);
                command.Parameters.AddWithValue("@value",
                    (object?)c.TriggerValue ?? DBNull.Value);
                command.Parameters.AddWithValue("@text",
                    (object?)c.TriggerText ?? DBNull.Value);
                command.Parameters.AddWithValue("@stationId",
                    (object?)c.StationId ?? DBNull.Value);
                command.Parameters.AddWithValue("@earthquakeId",
                    (object?)c.EarthquakeId ?? DBNull.Value);
                command.Parameters.AddWithValue("@basis",
                    (object?)c.LegalBasis ?? DBNull.Value);

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
}

/// <summary>
/// まだ対応が終わっていないアラート1件。
/// </summary>
public record PendingAlert(
    string SiteName,
    string EventType,
    DateTime OccurredAt,
    decimal? TriggerValue,
    string? TriggerText,
    string AlertStatus,
    int ElapsedHours);
