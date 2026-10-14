using ScaffoldAlert.Core.Forecast;

namespace ScaffoldAlert.Web.Models;

/// <summary>
/// 予測確認の画面に渡す情報。
/// </summary>
public class ForecastViewModel
{
    /// <summary>気象予測の取得結果。</summary>
    public ForecastFetchResult Result { get; set; } = new();

    /// <summary>取得にかかった時間 (秒)。</summary>
    public double Seconds { get; set; }

    /// <summary>この画面を開いた時刻。基準に達する予測を絞るのに使う。</summary>
    public DateTime Now { get; set; } = DateTime.Now;

    /// <summary>電文と風の予報を合わせた件数。</summary>
    public int TotalRows => Result.Rows.Count + Result.WindTexts.Count;
}
