// このファイルの目的:
//   Excelから書き出した日付が、日付文字列とシリアル値のどちらでも読めるようにします。
//
// なぜ必要か:
//   GoldStandardのSources.publication_dateは、元のxlsxでは46023のような
//   Excelのシリアル値（1899-12-30からの経過日数）です。
//   変換スクリプトで日付文字列へ直しますが、
//   手作業で書き出したCSVがシリアル値のまま渡ってくることは十分あります。
//   どちらでも読めるようにして、資料の発行日が画面から消えないようにします。
//
// 修正時の影響:
//   小さな整数までシリアル値として扱うと、無関係な数値が1900年の日付に化けます。
//   下限・上限を外さないでください。

using System.Globalization;

namespace Bui.Data.GoldStandard;

/// <summary>日付文字列とExcelシリアル値の両方を解釈します。</summary>
internal static class ExcelSerialDate
{
    /// <summary>Excelのシリアル値1が1900-01-01になる基準日です。</summary>
    private static readonly DateTimeOffset Epoch = new(1899, 12, 30, 0, 0, 0, TimeSpan.Zero);

    /// <summary>シリアル値として扱う範囲です。おおよそ1954年から2064年に相当します。</summary>
    private const int MinimumSerial = 20000;
    private const int MaximumSerial = 60000;

    /// <summary>読み取れない場合はnullを返します。例外は投げません。</summary>
    public static DateTimeOffset? Parse(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return null;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var serial))
        {
            return serial is >= MinimumSerial and <= MaximumSerial ? Epoch.AddDays(serial) : null;
        }

        var styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, styles, out var parsed)
            || DateTimeOffset.TryParse(text, CultureInfo.GetCultureInfo("ja-JP"), styles, out parsed))
        {
            return parsed;
        }

        return null;
    }
}
