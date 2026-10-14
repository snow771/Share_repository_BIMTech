using System.Globalization;

namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// 気象庁のCSVに書かれている値を、数値や日時に直す。
/// </summary>
public static class JmaValue
{
    /// <summary>
    /// 気象庁が「観測値なし」を表すのに使う書き方。
    /// これらは欠測として扱い、0 とは区別する。
    /// 0 で埋めてしまうと「雨が降らなかった」という誤った記録になる。
    /// </summary>
    private static readonly string[] MissingMarks =
        { "", "-", "--", "///", "×" };

    /// <summary>数値に直す。欠測や記号なら空を返す。</summary>
    public static decimal? ToDecimal(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string s = text.Trim();
        foreach (var mark in MissingMarks)
        {
            if (s == mark)
            {
                return null;
            }
        }

        if (decimal.TryParse(s, NumberStyles.Any,
                             CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        return null;
    }

    /// <summary>整数に直す。欠測や記号なら空を返す。</summary>
    public static int? ToInt(string? text)
    {
        var v = ToDecimal(text);
        return v.HasValue ? (int)v.Value : null;
    }

    /// <summary>
    /// 年月日時分から観測時刻を組み立てる。
    ///
    /// 気象庁は24時表記を使う。24時は翌日の0時として解釈する。
    /// そのまま DateTime に渡すと例外になるため。
    /// </summary>
    public static DateTime? BuildDateTime(int? year, int? month, int? day,
                                          int? hour, int? minute)
    {
        if (year is null || month is null || day is null
            || hour is null || minute is null)
        {
            return null;
        }

        try
        {
            if (hour == 24)
            {
                var next = new DateTime(year.Value, month.Value, day.Value)
                    .AddDays(1);
                return next.AddMinutes(minute.Value);
            }
            return new DateTime(year.Value, month.Value, day.Value,
                                hour.Value, minute.Value, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
