namespace ScaffoldAlert.Core.Weather;

/// <summary>1観測所の降水量。</summary>
public class PrecipRecord
{
    /// <summary>観測時刻。</summary>
    public DateTime? ObservedAt { get; set; }

    /// <summary>1時間降水量(mm)。</summary>
    public decimal? Precip1h { get; set; }

    /// <summary>12時間降水量(mm)。大雨の判定に使う。</summary>
    public decimal? Precip12h { get; set; }

    /// <summary>24時間降水量(mm)。</summary>
    public decimal? Precip24h { get; set; }

    /// <summary>品質情報。</summary>
    public string? QualityFlag { get; set; }
}

/// <summary>1観測所の風速。値とその起時(実際に吹いた時刻)。</summary>
public record WindRecord(decimal? Speed, DateTime? At);

/// <summary>1観測所の雪。</summary>
public record SnowRecord(decimal? Snow24h, decimal? SnowDepth);

/// <summary>
/// 気象データ取得の結果。画面に出して Python版と比べるために使う。
/// </summary>
public class WeatherFetchResult
{
    /// <summary>降水量。観測所番号をキーにする。</summary>
    public Dictionary<string, PrecipRecord> Precip { get; set; } = new();

    /// <summary>最大風速。</summary>
    public Dictionary<string, WindRecord> Wind { get; set; } = new();

    /// <summary>最大瞬間風速。</summary>
    public Dictionary<string, WindRecord> Gust { get; set; } = new();

    /// <summary>雪。</summary>
    public Dictionary<string, SnowRecord> Snow { get; set; } = new();

    /// <summary>風速CSVが示していた現在時刻。</summary>
    public DateTime? WindCurrentAt { get; set; }

    /// <summary>雪を取ったときの観測時刻。</summary>
    public DateTime? SnowObservedAt { get; set; }

    /// <summary>取得ごとの結果。うまくいかなかったものはここに理由が入る。</summary>
    public Dictionary<string, string> Errors { get; set; } = new();

    /// <summary>監視対象の観測所数。</summary>
    public int TargetStationCount { get; set; }

    /// <summary>12時間降水量が取れた地点数。</summary>
    public int Precip12hCount =>
        Precip.Values.Count(p => p.Precip12h.HasValue);

    /// <summary>最大風速が取れた地点数。</summary>
    public int WindCount => Wind.Values.Count(v => v.Speed.HasValue);

    /// <summary>最大風速の起時が取れた地点数。</summary>
    public int WindAtCount => Wind.Values.Count(v => v.At.HasValue);

    /// <summary>最大瞬間風速が取れた地点数。</summary>
    public int GustCount => Gust.Values.Count(v => v.Speed.HasValue);

    /// <summary>最大瞬間風速の起時が取れた地点数。</summary>
    public int GustAtCount => Gust.Values.Count(v => v.At.HasValue);

    /// <summary>24時間降雪量が取れた地点数。</summary>
    public int Snow24hCount => Snow.Values.Count(v => v.Snow24h.HasValue);
}
