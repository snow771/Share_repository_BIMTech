using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Web.Models;

/// <summary>
/// アラート対応の画面に渡す情報。
/// </summary>
public class AlertResponseViewModel
{
    /// <summary>対応するアラート。見つからなければ空。</summary>
    public AlertDetail? Alert { get; set; }

    /// <summary>うまくいかなかったときの理由。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>登録できたときの知らせ。</summary>
    public string SuccessMessage { get; set; } = string.Empty;

    /// <summary>
    /// 開いておくタブ。
    /// 入力し直しになったときに、同じタブへ戻すために使う。
    /// </summary>
    public string OpenTab { get; set; } = "instruct";

    /// <summary>入力し直しのときに、書いた内容を残す。</summary>
    public Dictionary<string, string> Input { get; set; } = new();

    /// <summary>入力欄の中身を取り出す。無ければ空。</summary>
    public string Value(string name)
        => Input.TryGetValue(name, out var v) ? v : string.Empty;
}
