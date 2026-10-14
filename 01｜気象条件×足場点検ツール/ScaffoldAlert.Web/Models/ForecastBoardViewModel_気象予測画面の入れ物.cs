using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Forecast;

namespace ScaffoldAlert.Web.Models;

/// <summary>
/// 気象予測の画面に渡す情報。
/// </summary>
public class ForecastBoardViewModel
{
    /// <summary>読み込めたかどうか。</summary>
    public bool Loaded { get; set; }

    /// <summary>読み込めなかったときの理由。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>いま見ている種類 (風 / 雨 / 雪)。</summary>
    public string Kind { get; set; } = "風";

    /// <summary>気象庁がこの予測を発表した日時のうち、最も新しいもの。</summary>
    public DateTime? ReportedAt { get; set; }

    /// <summary>
    /// この画面を開いた時刻。
    ///
    /// 予測の時間帯が「これからの話」なのか「もう過ぎた話」なのかは、
    /// いまが何時かが分からないと判断できない。
    /// </summary>
    public DateTime Now { get; set; } = DateTime.Now;

    /// <summary>予測が1件も無いかどうか。</summary>
    public bool IsEmpty { get; set; }

    /// <summary>部署ごとの、予測の区域との紐付け状況。</summary>
    public List<ForecastCoverage> Coverage { get; set; } = new();

    /// <summary>区域に紐付いていない現場の合計。</summary>
    public int UnlinkedTotal => Coverage.Sum(c => c.Unlinked);

    /// <summary>基準に達する予測。</summary>
    public List<OverLimitRow> OverLimit { get; set; } = new();

    /// <summary>基準に達する予測がある現場の数。</summary>
    public int OverLimitSiteCount
        => OverLimit.Select(o => o.SiteName).Distinct().Count();

    /// <summary>警報級の可能性 (現場と種類ごとに1行)。</summary>
    public List<PossibilityRow> Possibilities { get; set; } = new();

    /// <summary>
    /// 風の予報 (現場ごとに1行)。
    ///
    /// 気象庁は風の【高】【中】をほとんど発表しないため、
    /// 上の Possibilities には風がほぼ出てこない。
    /// 地図は風の強さで色を塗っているので、
    /// 同じ根拠の一覧をこちらに用意する。
    /// </summary>
    public List<WindRow> Winds { get; set; } = new();

    /// <summary>「注意」以上の風が予報されている現場の数。</summary>
    public int WindSiteCount => Winds.Count;

    /// <summary>判定と同じ基準の数字。</summary>
    public decimal WindLimit { get; set; } = 10m;
    public decimal RainLimit { get; set; } = 50m;
    public decimal SnowLimit { get; set; } = 25m;

    /// <summary>
    /// 地図に渡す中身 (区域ごとの警戒度と現場)。JSON の文字にしてある。
    /// </summary>
    public string MapDataJson { get; set; } = "{}";

    /// <summary>
    /// 地図に出る現場の部署。絞り込みの選択肢に使う。
    /// </summary>
    public List<string> SiteDepartments { get; set; } = new();

    /// <summary>
    /// 地図に出す現場の数。
    ///
    /// 画面に出しておくと、丸が見えないときに
    /// 「小さくて見えない」のか「そもそも渡っていない」のかを
    /// すぐ切り分けられる。
    /// </summary>
    public int SiteMarkerCount { get; set; }

    /// <summary>
    /// 同じ名前で別に登録されている現場の名前。
    ///
    /// 設計と工事で別々に登録されている場合などに起こる。
    /// 一覧に同じ名前が並んで見えるため、見間違いか実在かを
    /// 画面で切り分けられるようにする。
    /// </summary>
    public List<string> DuplicateNames { get; set; } = new();
}
