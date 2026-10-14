using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Web.Models;

/// <summary>
/// 「記録を直す」画面に渡す情報。
///
/// アラート対応の画面 (AlertResponseViewModel) と分けている。
/// あちらは「これから記録する」ための入れ物、
/// こちらは「すでにある記録を直す」ための入れ物で、
/// 出すものも守るべき決まりも違うため。
/// </summary>
public class AlertEditViewModel
{
    /// <summary>直すアラート。見つからなければ空。</summary>
    public AlertDetail? Alert { get; set; }

    /// <summary>
    /// そのアラートに付いている点検記録。まだ点検していなければ空。
    ///
    /// 指示を出しても、すぐに点検ができるとは限らない。
    /// 点検が済んでから、あとで書き足すことがある。
    /// </summary>
    public InspectionDetail? Inspection { get; set; }

    /// <summary>うまくいかなかったときの理由。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>指示の欄を直せるか。</summary>
    /// <remarks>
    /// 未対応のものは、まだ指示していないので直すものがない。
    /// </remarks>
    public bool CanEditInstruction
        => Alert is not null
           && (Alert.AlertStatus == "INSTRUCTED"
               || Alert.AlertStatus == "COMPLETED"
               || Alert.AlertStatus == "NOT_REQUIRED");

    /// <summary>点検の欄を直せるか。</summary>
    public bool CanEditInspection => Inspection is not null;

    /// <summary>対象外にした記録か。指示の欄の言い方が変わる。</summary>
    public bool IsNotRequired
        => Alert is not null && Alert.AlertStatus == "NOT_REQUIRED";

    /// <summary>
    /// 直すところが1つもないか。
    /// 未対応で点検もまだのものは、直す先がない。
    /// </summary>
    public bool NothingToEdit => !CanEditInstruction && !CanEditInspection;

    /// <summary>
    /// 対象外にした理由。頭の「[対象外] 」を外して返す。
    /// 印は保存の都合で付けているもので、読む人には関係がない。
    /// </summary>
    public string SkipReason
    {
        get
        {
            string note = Alert?.InstructionNote ?? string.Empty;
            const string mark = "[対象外] ";

            return note.StartsWith(mark) ? note.Substring(mark.Length) : note;
        }
    }
}
