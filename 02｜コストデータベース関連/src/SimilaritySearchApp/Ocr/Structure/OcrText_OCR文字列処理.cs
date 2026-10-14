using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SimilaritySearchApp.Ocr.Structure;

/// <summary>
/// OCR 文字列・Layout JSON の共通処理（Python 版 v6 の clean_text / normalize_label / to_number / get_table_matrix など）。
/// </summary>
public static class OcrText
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SpaceInNumber = new(@"(?<=[0-9.\-])\s+(?=[0-9.\-])", RegexOptions.Compiled);
    private static readonly Regex NumberOnly = new(@"^[-+]?[0-9]+(?:\.[0-9]+)?$", RegexOptions.Compiled);

    /// <summary>連続する空白を 1 つにして前後を削る。</summary>
    public static string Clean(string? value) => Spaces.Replace(value ?? "", " ").Trim();

    /// <summary>見出し比較用: 空白を除き、全角かっこ・中黒の表記ゆれを揃える。</summary>
    public static string NormalizeLabel(string? value) =>
        Clean(value).Replace(" ", "").Replace("　", "")
            .Replace("（", "(").Replace("）", ")")
            .Replace("･", "・").Replace("·", "・");

    /// <summary>
    /// OCR の数値を数にする。カンマ・円記号を除き、数字の間に混ざった空白を詰める
    /// （例: "79. 30" → 79.30 / "2, 000" → 2000）。数として読めなければ null。
    /// </summary>
    public static double? ToNumber(string? value)
    {
        var text = Clean(value);
        if (text.Length == 0) return null;
        text = ToAsciiDigits(text)
            .Replace(",", "").Replace("¥", "").Replace("￥", "")
            .Replace("円", "").Replace("−", "-").Replace("―", "-");
        string? previous = null;
        while (text != previous)
        {
            previous = text;
            text = SpaceInNumber.Replace(text, "");
        }
        return NumberOnly.IsMatch(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>全角数字（Python の float() は読める）を半角にする。</summary>
    private static string ToAsciiDigits(string s)
    {
        if (!s.Any(c => c is >= '０' and <= '９' or '．' or '－' or '＋')) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                >= '０' and <= '９' => (char)('0' + (c - '０')),
                '．' => '.',
                '－' => '-',
                '＋' => '+',
                _ => c,
            });
        }
        return sb.ToString();
    }

    /// <summary>Python の round()（偶数丸め）で整数にする。</summary>
    public static long Round(double v) => (long)Math.Round(v, MidpointRounding.ToEven);

    public static bool MatchesAny(string text, IEnumerable<string> patterns) =>
        patterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase));

    // ── Layout JSON ──

    /// <summary>表を行×列の文字列に展開する（結合セルは左上のセルにだけ入る）。</summary>
    public static List<string[]> TableMatrix(JsonNode table)
    {
        var rows = Int(table["rowCount"]);
        var cols = Int(table["columnCount"]);
        var matrix = Enumerable.Range(0, Math.Max(0, rows)).Select(_ => Enumerable.Repeat("", Math.Max(0, cols)).ToArray()).ToList();
        foreach (var cell in table["cells"] as JsonArray ?? new JsonArray())
        {
            if (cell is null) continue;
            var r = Int(cell["rowIndex"]);
            var c = Int(cell["columnIndex"]);
            if (r >= 0 && r < rows && c >= 0 && c < cols) matrix[r][c] = Clean((string?)cell["content"]);
        }
        return matrix;
    }

    /// <summary>表のあるページ（最初の領域のページ番号）。</summary>
    public static int? PageNumber(JsonNode table)
    {
        var regions = table["boundingRegions"] as JsonArray;
        if (regions is null || regions.Count == 0) return null;
        return regions[0]?["pageNumber"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;
    }

    public static IEnumerable<JsonNode> Tables(JsonNode layout) =>
        (layout["tables"] as JsonArray ?? new JsonArray()).OfType<JsonNode>();

    public static IEnumerable<JsonNode> Pages(JsonNode layout) =>
        (layout["pages"] as JsonArray ?? new JsonArray()).OfType<JsonNode>();

    public static int Int(JsonNode? v)
    {
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<double>(out var d)) return (int)d;
            if (jv.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) return p;
        }
        return 0;
    }
}
