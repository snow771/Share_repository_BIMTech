using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Web.Models;

/// <summary>
/// 点検記録簿の画面に渡す情報。
/// </summary>
public class AuditViewModel
{
    /// <summary>読み込めたかどうか。</summary>
    public bool Loaded { get; set; }

    /// <summary>読み込めなかったときの理由。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>直したあとの知らせ。</summary>
    public string SuccessMessage { get; set; } = string.Empty;

    /// <summary>期間の開始。</summary>
    public DateTime From { get; set; }

    /// <summary>期間の終了。</summary>
    public DateTime To { get; set; }

    /// <summary>
    /// 読み込んだ記録すべて。対象外のものも含む。
    ///
    /// 画面には Rows と Skipped に分けて出す。
    /// CSV は、こちらの全部を書き出す。
    /// </summary>
    public List<AuditRow> All { get; set; } = new();

    /// <summary>
    /// 表に出す記録。対象外は含まない。
    ///
    /// ★なぜ対象外を外すか
    ///   対象外は「足場を撤去済み」「試験のデータ」など、
    ///   そもそも点検する必要がなかったもの。
    ///   これを混ぜたまま「未実施」に数えると、
    ///   実際にはやるべきことが残っていないのに
    ///   未実施の件数が膨らみ、本当に危ない件が埋もれる。
    ///
    ///   消してはいない。下の折りたたみに残してある。
    ///   臨検で「なぜ点検しなかったのか」を問われたときに、
    ///   理由を出せなければ意味がないため。
    /// </summary>
    public List<AuditRow> Rows => All.Where(r => !r.IsNotRequired).ToList();

    /// <summary>対象外にした記録。折りたたんで下に出す。</summary>
    public List<AuditRow> Skipped => All.Where(r => r.IsNotRequired).ToList();

    /// <summary>点検が済んでいる件数。対象外は数えない。</summary>
    public int DoneCount => Rows.Count(r => r.IsDone);

    /// <summary>点検がまだの件数。対象外は数えない。</summary>
    public int NotDoneCount => Rows.Count - DoneCount;

    /// <summary>対象外にした件数。</summary>
    public int SkippedCount => Skipped.Count;
}
