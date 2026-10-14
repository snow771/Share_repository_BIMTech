using System.Globalization;

namespace SimilaritySearchApp.Services;

/// <summary>文字列の正規化（Python 版の _norm_key / _normalize / 列の strip 処理に相当）。</summary>
public static class TextNormalizer
{
    /// <summary>
    /// DB 値を表示・比較用の文字列にする。全角空白→半角空白、前後空白除去。
    /// null・DBNull・"nan"/"none"/"null" は空文字。
    /// </summary>
    public static string Clean(object? value)
    {
        if (value is null || value is DBNull) return "";
        var s = value switch
        {
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        s = s.Replace('　', ' ').Trim();
        var lower = s.ToLowerInvariant();
        return lower is "nan" or "none" or "null" ? "" : s;
    }

    /// <summary>Clean の結果が空なら null。</summary>
    public static string? CleanOrNull(object? value)
    {
        var s = Clean(value);
        return s.Length == 0 ? null : s;
    }

    /// <summary>
    /// 照合キー（Python の _norm_key）。小文字化、全角括弧→半角、中黒・半角中黒・ハイフン・空白を除去。
    /// </summary>
    public static string NormKey(string? s)
    {
        var t = (s ?? "").Trim().ToLowerInvariant().Replace('　', ' ');
        t = t.Replace('（', '(').Replace('）', ')');
        t = t.Replace("・", "").Replace("･", "").Replace("-", "");
        t = t.Replace(" ", "");
        return t;
    }

    /// <summary>ゆるい正規化（Python の _normalize）。小文字化、全角空白→半角、全角括弧→半角。</summary>
    public static string NormLoose(string? s)
    {
        var t = (s ?? "").Trim().ToLowerInvariant();
        t = t.Replace('　', ' ');
        t = t.Replace('（', '(').Replace('）', ')');
        return t;
    }

    /// <summary>空・null は default に置き換える（Python の coalesce_text）。</summary>
    public static string Coalesce(string? value, string defaultValue = Models.FilterCriteria.Unspecified)
    {
        if (value is null) return defaultValue;
        var s = value.Trim();
        return s.Length > 0 ? s : defaultValue;
    }
}
