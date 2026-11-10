// このファイルの目的:
//   GoldStandardの元データに混ざっている取り込み事故と表記ゆれを、
//   画面へ出す前に一か所で落とします。
//
// なぜ264を消すのか:
//   value_stringに264が120件現れますが、その全件がvalue_type=numberの行、
//   つまり文字列値を持たない行です。note・table_no・source_textにも同じ値が出ます。
//   元Excelからの取り込み事故と判断しました。
//   ただし「264を含む値」（NM-264、2640）は本物なので消してはいけません。
//   完全一致だけを対象にします。
//
// 修正時の影響:
//   ここを緩めると概要カードの数値欄に264が並びます。
//   ここを厳しくしすぎると本物の値が消えます。判定は完全一致のまま保ってください。
//
// なぜ末尾の0を落とすのか:
//   SQL Serverの列は小数10桁のため、CSVでは100.0だった値が100.0000000000になります。
//   桁数を固定して丸める案は採りません。0.225を0.23にすると別の値になります。

using System.Globalization;

namespace Bui.Data.GoldStandard;

/// <summary>属性値の整形をまとめた純粋関数です。</summary>
internal static class SpecValueFormatter
{
    /// <summary>元Excelの取り込み事故で入り込んだ無効値です。</summary>
    private const string InvalidPlaceholder = "264";

    /// <summary>単位として意味を持たない表記です。表示しません。</summary>
    private static readonly string[] EmptyUnits = { "-", "ー", "－", "‐", "—" };

    /// <summary>前後の空白を落とし、無効値を空欄へ変えます。</summary>
    public static string Clean(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.Equals(trimmed, InvalidPlaceholder, StringComparison.Ordinal) ? string.Empty : trimmed;
    }

    /// <summary>
    /// 数値として読めれば桁区切りを付けます。読めなければ元の文字列を返します。
    /// 実データのareal_weightには「8.1～11.7」という範囲文字列があり、これを落としてはいけません。
    /// </summary>
    public static string FormatNumber(string? raw)
    {
        var value = Clean(raw);
        if (value.Length == 0) return string.Empty;

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return value;
        }

        // 意味のある小数桁だけを残します。SQL Serverの列は小数10桁のため、
        // 元の桁数をそのまま保つと 100.0000000000 と表示されます。
        // 丸めは行いません。0.22を0.2にすると別の値になります。落とすのは末尾の0だけです。
        var separatorIndex = value.IndexOf('.');
        var decimals = 0;
        if (separatorIndex >= 0)
        {
            var fraction = value.Substring(separatorIndex + 1).TrimEnd('0');
            decimals = fraction.Length;
        }

        return number.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>単位を表示できる形へ整えます。意味を持たない記号は空欄にします。</summary>
    public static string NormalizeUnit(string? unit)
    {
        var value = Clean(unit);
        return EmptyUnits.Contains(value, StringComparer.Ordinal) ? string.Empty : value;
    }

    /// <summary>
    /// value_numberとvalue_stringのうち、実際に値がある方を選びます。
    /// value_typeは空欄の行があるため（実データで20件）、型ではなく中身で判断します。
    /// </summary>
    public static string SelectValue(string? valueNumber, string? valueString)
    {
        var number = Clean(valueNumber);
        return number.Length > 0 ? FormatNumber(number) : Clean(valueString);
    }
}
