using System.Globalization;
using System.Text.RegularExpressions;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>選択肢1件と、その値を選んだときの該当件数。</summary>
public sealed record FacetOption(string Value, int Count);

/// <summary>
/// 物件の絞込（Python の apply_project_filters / _apply_area_range_filter / make_filter_options）と、
/// 左パネルに出す選択肢＋件数（ファセット）の生成。
/// </summary>
public static class ProjectFilterService
{
    public const string FacetPrefecture = "prefecture";
    public const string FacetCity = "city";
    public const string FacetTown = "town";
    public const string FacetStructure = "structure";
    public const string FacetForm = "form";
    public const string FacetFloorsAbove = "floorsAbove";
    public const string FacetFloorsBelow = "floorsBelow";

    private static readonly Regex StartsWithDigit = new(@"^\d", RegexOptions.Compiled);
    private static readonly Regex AsciiLettersOnly = new(@"^[A-Za-z_]+$", RegexOptions.Compiled);
    private static readonly Regex NumericLike = new(@"^[\d.,]+\s*[tTmMkKgG%]?$", RegexOptions.Compiled);

    /// <summary>属性条件と（includeArea のとき）面積レンジで絞る。</summary>
    public static List<ProjectRecord> Apply(IEnumerable<ProjectRecord> projects, FilterCriteria c, bool includeArea = true) =>
        projects.Where(p => Matches(p, c, skip: null, includeArea)).ToList();

    /// <summary>
    /// 形態の値を正規化する（Python の _clean_type）。
    /// 空・null系、数字で始まる値（10t など）、英字のみ（PumpTruck など）は null。
    /// </summary>
    public static string? CleanForm(string? value)
    {
        var v = TextNormalizer.Clean(value);
        if (v.Length == 0) return null;
        if (StartsWithDigit.IsMatch(v)) return null;
        if (AsciiLettersOnly.IsMatch(v)) return null;
        return v;
    }

    /// <summary>選択肢一覧。先頭は (未指定)、以降は重複除去して序数順。</summary>
    public static List<string> MakeOptions(IEnumerable<string?> values, bool excludeNumeric = false)
    {
        var set = values
            .Select(v => TextNormalizer.Clean(v))
            .Where(v => v.Length > 0)
            .Where(v => !excludeNumeric || !IsNumericLike(v))
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        set.Insert(0, FilterCriteria.Unspecified);
        return set;
    }

    /// <summary>階数の表示値。整数なら小数点なし（7.0 → "7"）。</summary>
    public static string FormatFloor(double? value)
    {
        if (value is not double d || !double.IsFinite(d)) return "";
        return d == Math.Floor(d)
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 左パネルの各選択肢と件数。各ファセットは「自分と下位階層以外の条件」で絞った物件を数える。
    /// （地域は 都道府県 → 市区町村 → 町名 の階層。上位を変えられるよう下位条件は無視する）
    /// 件数0の値は、現在選択中の値を除いて出さない。
    /// </summary>
    public static Dictionary<string, List<FacetOption>> BuildFacets(IReadOnlyList<ProjectRecord> all, FilterCriteria c)
    {
        return new Dictionary<string, List<FacetOption>>
        {
            [FacetPrefecture] = Facet(all, c, FacetPrefecture, p => p.Prefecture, c.Prefecture),
            [FacetCity] = Facet(all, c, FacetCity, p => p.City, c.City),
            [FacetTown] = Facet(all, c, FacetTown, p => p.Town, c.Town),
            [FacetStructure] = Facet(all, c, FacetStructure, p => p.Structure, c.Structure),
            [FacetForm] = Facet(all, c, FacetForm, p => p.Form, c.Form, excludeNumeric: true),
            [FacetFloorsAbove] = Facet(all, c, FacetFloorsAbove, p => FormatFloor(p.FloorsAbove), c.FloorsAbove, numericSort: true),
            [FacetFloorsBelow] = Facet(all, c, FacetFloorsBelow, p => FormatFloor(p.FloorsBelow), c.FloorsBelow, numericSort: true),
        };
    }

    /// <summary>基準面積の最小・最大。値が無ければ null。</summary>
    public static (double Min, double Max)? AreaBounds(IEnumerable<ProjectRecord> projects, BasisMode mode)
    {
        var areas = projects.Select(p => p.GetArea(mode))
            .Where(a => a is double d && double.IsFinite(d))
            .Select(a => a!.Value)
            .ToList();
        return areas.Count == 0 ? null : (areas.Min(), areas.Max());
    }

    // ------------------------------------------------------------------

    private static bool IsNumericLike(string v) =>
        NumericLike.IsMatch(v) || v.ToLowerInvariant() is "nan" or "none" or "null" or "---";

    private static List<FacetOption> Facet(
        IReadOnlyList<ProjectRecord> all, FilterCriteria c, string facet,
        Func<ProjectRecord, string?> selector, string selected,
        bool excludeNumeric = false, bool numericSort = false)
    {
        var counts = new Dictionary<string, int>();
        foreach (var p in all)
        {
            if (!Matches(p, c, facet, includeArea: true)) continue;
            var v = TextNormalizer.Clean(selector(p));
            if (v.Length == 0) continue;
            if (excludeNumeric && IsNumericLike(v)) continue;
            counts[v] = counts.TryGetValue(v, out var n) ? n + 1 : 1;
        }
        if (FilterCriteria.IsSet(selected) && !counts.ContainsKey(selected))
            counts[selected] = 0;

        IEnumerable<KeyValuePair<string, int>> ordered = numericSort
            ? counts.OrderBy(kv => double.TryParse(kv.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.MaxValue)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            : counts.OrderBy(kv => kv.Key, StringComparer.Ordinal);
        return ordered.Select(kv => new FacetOption(kv.Key, kv.Value)).ToList();
    }

    /// <summary>skip に指定したファセット（と地域の下位階層）の条件は無視して判定する。</summary>
    private static bool Matches(ProjectRecord p, FilterCriteria c, string? skip, bool includeArea)
    {
        bool skipPref = skip == FacetPrefecture;
        bool skipCity = skipPref || skip == FacetCity;
        bool skipTown = skipCity || skip == FacetTown;

        if (!skipPref && !Eq(p.Prefecture, c.Prefecture)) return false;
        if (!skipCity && !Eq(p.City, c.City)) return false;
        if (!skipTown && !Eq(p.Town, c.Town)) return false;
        if (skip != FacetStructure && !Eq(p.Structure, c.Structure)) return false;
        if (skip != FacetForm && !Eq(p.Form, c.Form)) return false;
        if (skip != FacetFloorsAbove && !FloorEq(p.FloorsAbove, c.FloorsAbove)) return false;
        if (skip != FacetFloorsBelow && !FloorEq(p.FloorsBelow, c.FloorsBelow)) return false;

        if (includeArea && (c.AreaMin.HasValue || c.AreaMax.HasValue))
        {
            // Python と同様、面積が欠損の物件は範囲外
            if (p.GetArea(c.BasisMode) is not double a || !double.IsFinite(a)) return false;
            if (c.AreaMin is double mn && a < mn) return false;
            if (c.AreaMax is double mx && a > mx) return false;
        }
        return true;
    }

    private static bool Eq(string? value, string criterion) =>
        !FilterCriteria.IsSet(criterion) || TextNormalizer.Clean(value) == criterion;

    private static bool FloorEq(double? value, string criterion)
    {
        if (!FilterCriteria.IsSet(criterion)) return true;
        if (value is not double v) return false;
        return double.TryParse(criterion, NumberStyles.Float, CultureInfo.InvariantCulture, out var target)
            ? v == target
            : FormatFloor(v) == criterion;
    }
}
