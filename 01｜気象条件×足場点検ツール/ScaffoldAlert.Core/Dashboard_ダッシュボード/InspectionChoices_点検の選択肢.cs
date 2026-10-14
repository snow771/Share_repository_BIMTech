using System.Diagnostics.CodeAnalysis;

namespace ScaffoldAlert.Core.Dashboard;

/// <summary>
/// 点検記録を登録するときの選択肢。
///
/// 自由に書けるようにすると表記がばらつき、
/// 臨検で並べたときに読み取りにくくなる。
/// 決められたものから選ぶ形にしている。
/// </summary>
public static class InspectionChoices
{
    /// <summary>足場種類。</summary>
    public static readonly string[] ScaffoldTypes =
    {
        "くさび緊結式足場",
        "わく組足場",
        "単管足場",
        "吊り足場",
        "その他",
    };

    /// <summary>
    /// 点検結果。画面の言葉 → データベースに入れる記号。
    /// 記号はテーブルの決まり (CK_T_Inspection_Result) で
    /// この3つに限られている。
    /// </summary>
    public static readonly Dictionary<string, string> Results = new()
    {
        ["異常なし"] = "OK",
        ["異常あり(補修済)"] = "NG_FIXED",
        ["異常あり(未補修)"] = "NG_OPEN",
    };

    /// <summary>記号 → 画面の言葉。点検記録簿に出すときに使う。</summary>
    public static string ResultLabel(string? code)
    {
        foreach (var pair in Results)
        {
            if (pair.Value == code)
            {
                return pair.Key;
            }
        }
        return code ?? string.Empty;
    }

    /// <summary>
    /// 画面の言葉が、決められたものかどうか。
    /// </summary>
    /// <remarks>
    /// ★[NotNullWhen(true)] を付けている理由
    ///   これが true を返したなら label は空ではない、と
    ///   コンパイラに教えるための印。
    ///
    ///   付けていないと、確かめたあとで使うところで
    ///   「空かもしれません」(CS8604) と言われてしまう。
    ///   人が見れば確かめ済みだと分かるが、
    ///   コンパイラには伝わらないため。
    /// </remarks>
    public static bool IsKnownResult([NotNullWhen(true)] string? label)
        => label is not null && Results.ContainsKey(label);

    /// <summary>足場種類が、決められたものかどうか。</summary>
    public static bool IsKnownScaffold([NotNullWhen(true)] string? type)
        => type is not null && ScaffoldTypes.Contains(type);
}
