using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Web.Models;

/// <summary>
/// 取得確認の画面に渡す情報。
/// </summary>
public class FetchViewModel
{
    /// <summary>うまくいったかどうか。</summary>
    public bool Succeeded { get; set; }

    /// <summary>うまくいかなかったときの理由。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>地震の取得結果。</summary>
    public QuakeFetchResult Quake { get; set; } = new();

    /// <summary>いまデータベースに入っている直近の地震。</summary>
    public List<StoredQuake> Stored { get; set; } = new();

    /// <summary>降水量・風速・雪の取得結果。</summary>
    public WeatherFetchResult Weather { get; set; } = new();

    /// <summary>観測所番号から観測所名を引く表。</summary>
    public Dictionary<string, string> StationNames { get; set; } = new();

    /// <summary>取得にかかった時間 (秒)。</summary>
    public double Seconds { get; set; }

    /// <summary>取り込み対象の地域別震度の合計。</summary>
    public int TotalIntensities =>
        Quake.Targets.Sum(t => t.CityIntensities.Count);
}
