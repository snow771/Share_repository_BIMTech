namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// 気象庁から取ってきた地震1件。
/// </summary>
public class QuakeEntry
{
    /// <summary>気象庁が付けている地震のID (eid)。</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>発生日時 (at)。</summary>
    public DateTime? OccurredAt { get; set; }

    /// <summary>震源地 (anm)。</summary>
    public string? Hypocenter { get; set; }

    /// <summary>マグニチュード (mag)。</summary>
    public decimal? Magnitude { get; set; }

    /// <summary>震源の深さ (km)。</summary>
    public int? DepthKm { get; set; }

    /// <summary>全国の最大震度 (maxi)。</summary>
    public string? MaxIntensity { get; set; }

    /// <summary>市区町村コード(7桁) と そこで観測された震度。</summary>
    public Dictionary<string, string> CityIntensities { get; set; } = new();
}

/// <summary>取り込みの結果。画面に出して Python版と比べるために使う。</summary>
public class QuakeFetchResult
{
    /// <summary>気象庁が配信していた件数。</summary>
    public int DeliveredCount { get; set; }

    /// <summary>取り込みの対象になった件数 (新しいもの＋続報がありうるもの)。</summary>
    public int TargetCount { get; set; }

    /// <summary>震度の情報が無いため対象外にした件数。</summary>
    public int SkippedNoIntensity { get; set; }

    /// <summary>取り込み済みで、もう更新されないため飛ばした件数。</summary>
    public int SkippedKnown { get; set; }

    /// <summary>対象になった地震の中身。</summary>
    public List<QuakeEntry> Targets { get; set; } = new();

    /// <summary>うまくいかなかったときの理由。</summary>
    public string? ErrorMessage { get; set; }
}
