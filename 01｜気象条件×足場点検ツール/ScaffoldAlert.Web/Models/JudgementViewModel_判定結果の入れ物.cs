using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Judgement;

namespace ScaffoldAlert.Web.Models;

/// <summary>
/// 判定確認の画面に渡す情報をまとめた入れ物。
/// </summary>
public class JudgementViewModel
{
    /// <summary>データベースに繋がったかどうか。</summary>
    public bool Connected { get; set; }

    /// <summary>繋がらなかったときの理由。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>いま使っている判定の基準。</summary>
    public Dictionary<string, Threshold> Thresholds { get; set; } = new();

    /// <summary>判定の結果。</summary>
    public JudgeResult Result { get; set; } = new();

    /// <summary>判定にかかった時間 (秒)。</summary>
    public double Seconds { get; set; }

    /// <summary>
    /// まだ対応が終わっていないアラート。
    /// 今回の判定結果とは別物で、これまでに記録されたもの。
    /// </summary>
    public List<PendingAlert> Pending { get; set; } = new();

    /// <summary>対応の状況を日本語にする。</summary>
    public static string StatusLabel(string status) => status switch
    {
        "OPEN" => "未対応",
        "INSTRUCTED" => "指示済",
        "COMPLETED" => "点検完了",
        "NOT_REQUIRED" => "対象外",
        _ => status,
    };

    /// <summary>事象の種類を日本語にする。</summary>
    public static string EventLabel(string eventType) => eventType switch
    {
        "RAIN" => "大雨",
        "WIND" => "強風",
        "GUST" => "暴風",
        "SNOW" => "大雪",
        "EARTHQUAKE" => "地震",
        _ => eventType,
    };
}
