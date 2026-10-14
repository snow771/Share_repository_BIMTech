using System.Text.Json;

namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// 雪(降雪量・積雪深)を取ってくる係。
///
/// 雪だけは気象庁のCSVで配信されていないため、
/// アメダスの最新の観測値から取る。
/// </summary>
public class SnowFetcher
{
    private readonly IHttpClientFactoryLike _http;

    public SnowFetcher(IHttpClientFactoryLike http)
    {
        _http = http;
    }

    /// <summary>
    /// 監視対象の観測所の雪を取ってくる。
    /// </summary>
    /// <param name="observedAt">取ってきた値の観測時刻。</param>
    public Dictionary<string, SnowRecord> Fetch(
        HashSet<string> targetIds, out DateTime? observedAt)
    {
        // 例: 2026-08-26T15:10:00+09:00
        string raw = _http.GetString(JmaUrls.AmedasLatestTime).Trim();
        observedAt = EarthquakeFetcher.ToDateTime(raw);

        // 雪の項目は「正時」のデータにしか入っていない。
        // アメダスは10分ごとに更新されるが、15:10 のデータには雪の欄が
        // 1地点も無く、15:00 には全地点にある (実際に確認済み)。
        // そのため分を00に落として取りに行く。
        if (observedAt is not null)
        {
            observedAt = new DateTime(observedAt.Value.Year,
                                      observedAt.Value.Month,
                                      observedAt.Value.Day,
                                      observedAt.Value.Hour, 0, 0);
        }

        string stamp = observedAt is not null
            ? observedAt.Value.ToString("yyyyMMddHHmmss")
            : ToStamp(raw);

        string url = string.Format(JmaUrls.AmedasMap, stamp);
        string json = _http.GetString(url);

        var result = new Dictionary<string, SnowRecord>();

        using var doc = JsonDocument.Parse(json);
        foreach (var station in doc.RootElement.EnumerateObject())
        {
            string id = station.Name.Trim();
            if (!targetIds.Contains(id))
            {
                continue;
            }

            result[id] = new SnowRecord(
                Value(station.Value, "snow24h"),
                Value(station.Value, "snow"));
        }

        return result;
    }

    /// <summary>
    /// 日時の文字列から、URLに使う形 (20260826150000) を作る。
    /// 日時として読めなかったときの備え。
    /// </summary>
    private static string ToStamp(string raw)
        => raw.Replace("-", string.Empty)
              .Replace(":", string.Empty)
              .Replace("T", string.Empty)
              .Replace("+0900", string.Empty);

    /// <summary>
    /// アメダスの値を取り出す。
    ///
    /// 値は [数値, 品質フラグ] の形で入っている。
    /// 正常を表す 0 のときだけ採用する。
    /// 誤った値で点検義務を判定すると、臨検で説明できなくなるため。
    ///
    /// 雪の欄の品質フラグは実際に次のようになっていた (2026-08-26 の確認)。
    ///   0    … 正常
    ///   5    … 積雪深計の無効期間 (夏季)。値も入っていない
    ///   6    … 24時間降雪量が無効
    ///   無し … その観測所が雪を観測していない。値は 0 が入っているが
    ///          意味のある 0 ではないため採用しない
    /// </summary>
    public static decimal? Value(JsonElement record, string key)
    {
        if (!record.TryGetProperty(key, out var pair)
            || pair.ValueKind != JsonValueKind.Array
            || pair.GetArrayLength() < 2)
        {
            return null;
        }

        var flag = pair[1];
        if (flag.ValueKind != JsonValueKind.Number || flag.GetInt32() != 0)
        {
            return null;
        }

        var value = pair[0];
        if (value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.GetDecimal();
    }
}
