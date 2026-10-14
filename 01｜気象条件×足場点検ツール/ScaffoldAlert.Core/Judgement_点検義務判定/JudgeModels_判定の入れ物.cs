namespace ScaffoldAlert.Core.Judgement;

/// <summary>
/// しきい値マスタ (M_Threshold) の1行。
/// </summary>
public record Threshold(
    string EventType,
    string Name,
    decimal? Value,
    string? Text,
    int? WindowHours,
    string? LegalBasis);

/// <summary>
/// 「点検義務が発生した」と判定された1件。
///
/// この段階ではデータベースに書き込まず、画面に出すだけ。
/// Python版と結果が一致することを確認してから、書き込みへ進む。
/// </summary>
public record AlertCandidate
{
    /// <summary>現場のID。</summary>
    public int SiteId { get; init; }

    /// <summary>現場名。</summary>
    public string SiteName { get; init; } = string.Empty;

    /// <summary>事象の種類 (RAIN / WIND / GUST / SNOW / EARTHQUAKE)。</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>いつ起きたか。</summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>観測された数値 (雨量mm・風速m/s・降雪量cm)。地震では空。</summary>
    public decimal? TriggerValue { get; init; }

    /// <summary>文字の値 (震度)。雨や風では空。</summary>
    public string? TriggerText { get; init; }

    /// <summary>観測所の番号。地震では空。</summary>
    public string? StationId { get; init; }

    /// <summary>観測所名、または地震の地域名。</summary>
    public string? StationName { get; init; }

    /// <summary>地震のID。雨や風では空。</summary>
    public int? EarthquakeId { get; init; }

    /// <summary>法令の根拠。</summary>
    public string? LegalBasis { get; init; }

    /// <summary>画面に出す説明文。</summary>
    public string Detail { get; init; } = string.Empty;
}

/// <summary>判定の結果全体。</summary>
public class JudgeResult
{
    /// <summary>発報の対象になった現場。</summary>
    public List<AlertCandidate> Candidates { get; } = new();

    /// <summary>気をつけてほしいこと (想定外の震度表記など)。</summary>
    public List<string> Warnings { get; } = new();
}
