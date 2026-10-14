using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Dashboard;
using ScaffoldAlert.Core.Judgement;

namespace ScaffoldAlert.Web.Models;

/// <summary>
/// ダッシュボードの画面に渡す情報。
/// </summary>
public class DashboardViewModel
{
    /// <summary>データベースに繋がったかどうか。</summary>
    public bool Connected { get; set; }

    /// <summary>繋がらなかったときの理由。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>まとめて処理した結果の知らせ。</summary>
    public string BulkSuccess { get; set; } = string.Empty;

    /// <summary>まとめて処理できなかったときの理由。</summary>
    public string BulkError { get; set; } = string.Empty;

    /// <summary>稼働中の現場の数。</summary>
    public int ActiveSiteCount { get; set; }

    /// <summary>
    /// 点検義務が生じる基準。
    ///
    /// ★画面に書き写さず、データベースから読む
    ///   判定に使う数字と画面に出す数字が別々だと、
    ///   基準を変えたときに片方だけ古いままになる。
    ///   臨検で「画面の基準と実際の判定が違う」となるのは致命的。
    ///
    /// 読めなかったときは空のままにして、画面には出さない。
    /// 間違った基準を出すより、出さないほうがよい。
    /// </summary>
    public List<Threshold> Limits { get; set; } = new();

    /// <summary>地図に出す現場。</summary>
    public List<SiteMarker> Sites { get; set; } = new();

    /// <summary>対応が必要なアラート。</summary>
    public List<DashboardAlert> Alerts { get; set; } = new();

    /// <summary>
    /// 地図に渡す中身。JSON の文字にしてある。
    ///
    /// 画面のJavaScriptへ値を渡すのに、HTMLの中へ直接書くのではなく
    /// JSON にまとめて渡す。現場名に引用符などが含まれていても
    /// 画面が壊れないようにするため。
    /// </summary>
    public string MapDataJson { get; set; } = "{}";

    /// <summary>未対応の件数。</summary>
    public int OpenCount => Alerts.Count(a => a.AlertStatus == "OPEN");

    /// <summary>指示済(点検完了待ち)の件数。</summary>
    public int InstructedCount
        => Alerts.Count(a => a.AlertStatus == "INSTRUCTED");

    /// <summary>
    /// 判定基準を画面に並べる順。
    ///
    /// データベースの並び順のままだと、取り込んだ順に出てしまい、
    /// 画面を見るたびに並びが変わることがある。
    /// 起きる頻度の高い順に固定する。
    /// </summary>
    public static int LimitOrder(string? eventType) => eventType switch
    {
        "RAIN" => 1,
        "WIND" => 2,
        "GUST" => 3,
        "SNOW" => 4,
        "EARTHQUAKE" => 5,
        _ => 9,
    };

    /// <summary>
    /// 1つの基準を、画面に出す1行にする。
    ///
    /// 例  大雨 12時間で 50mm 以上
    ///     地震 震度4以上
    /// </summary>
    public static string LimitText(Threshold t)
    {
        string name = DisplaySettings.Event(t.EventType);

        // 地震だけは数値ではなく震度なので、言い方を分ける。
        if (t.Value is null)
        {
            return string.IsNullOrEmpty(t.Text)
                ? name
                : $"{name} 震度{t.Text}以上";
        }

        string span = t.WindowHours is > 0 ? $"{t.WindowHours}時間で " : "";
        string unit = DisplaySettings.Unit(t.EventType);

        return $"{name} {span}{t.Value:0.#}{unit} 以上";
    }
}
