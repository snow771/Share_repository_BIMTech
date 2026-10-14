using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SimilaritySearchApp.Excel;

namespace SimilaritySearchApp.Stencil;

/// <summary>
/// ① 物件諸情報の抽出（Python「stencilデータ変換_完成版.py」の extract_row_from_workbook を移植・整理）。
/// Stencile 1ファイル → 出力列（62列）の1行。
///
/// Python 版からの修正:
///   ・「1SLDK」の戸数が常に 0 になっていた（キーの全角/半角の不一致）のを修正
///   ・間取の戸数は「1R-10戸」「1LDK 5」「OR-2」などの表記に対応（従来どおり）
/// </summary>
public static class ProjectInfoExtractor
{
    /// <summary>出力列（Python 版の OUTPUT_COLUMNS と同じ。Excel 出力の見出しにも使う）</summary>
    public static readonly string[] OutputColumns =
    {
        "project_id", "物件名", "現場住所_都道府県", "現場住所_市区町村", "現場住所_町名",
        "請負金額", "契約時予定利益", "利益率", "敷地面積", "建築面積", "法廷床面積", "施工床面積",
        "施工坪単価", "工期(着工)", "工期(竣工)", "構造", "階数_地上", "階数_地下",
        "1R", "1K", "1DK", "1LDK", "2K", "2DK", "2LDK", "3K", "3DK", "3LDK",
        "4K", "4DK", "4LDK", "5K", "5DK", "5LDK", "1SK", "2SK", "１SLDK", "2SLDK", "3SLDK",
        "OR", "テナント", "店舗", "事務所", "その他", "間取", "世帯数", "形態", "ELV",
        "ｺﾝｸﾘｰﾄ", "型枠", "鉄筋（Kg/m2）", "鉄骨（Kg/m2）", "ＡＬＣ", "外壁タイル",
        "山留工法", "杭工事", "土工事", "型枠・鉄筋", "鉄骨", "ALC", "生ｺﾝ車", "ﾎﾟﾝﾌﾟ車",
    };

    /// <summary>間取の種類（戸数を数える）</summary>
    public static readonly string[] PlanKeys =
    {
        "1R", "1K", "1DK", "1LDK", "2K", "2DK", "2LDK", "3K", "3DK", "3LDK",
        "4K", "4DK", "4LDK", "5K", "5DK", "5LDK", "1SK", "2SK", "1SLDK", "2SLDK", "3SLDK",
    };

    /// <summary>用途（戸数・区画数を数える）</summary>
    public static readonly string[] CategoryKeys = { "OR", "テナント", "店舗", "事務所", "その他" };

    /// <summary>必須シート（無いファイルはエラー）</summary>
    public static readonly string[] RequiredSheets = { "実行予算表書", "実行予算・引継書", "原価推移表" };

    private const string Main = "実行予算表書";
    private const string Handover = "実行予算・引継書";
    private const string CostTrend = "原価推移表";
    private const double M2PerTsubo = 3.305785;

    private static readonly string[] MainFields =
    {
        "請負金額", "契約時予定利益", "利益率", "敷地面積", "建築面積", "法廷床面積", "施工床面積", "構造", "世帯数",
        "ｺﾝｸﾘｰﾄ", "型枠", "鉄筋（Kg/m2）", "鉄骨（Kg/m2）", "ＡＬＣ", "外壁タイル",
    };

    private static readonly string[] HandoverFields = { "山留工法", "杭工事", "土工事", "型枠・鉄筋", "鉄骨", "ALC", "生ｺﾝ車", "ﾎﾟﾝﾌﾟ車" };

    /// <summary>1ブック分を抽出する。必須シートが無ければ InvalidDataException。</summary>
    public static Dictionary<string, object?> Extract(IReadOnlyDictionary<string, SheetData> book, StencilMapping mapping)
    {
        var missing = RequiredSheets.Where(s => !book.ContainsKey(s)).ToList();
        if (missing.Count > 0) throw new InvalidDataException("必要なシートがありません: " + string.Join("、", missing));

        var row = OutputColumns.ToDictionary(c => c, _ => (object?)null, StringComparer.Ordinal);
        object? Get(string section, string field) => mapping.Read(book, section, field);

        row["project_id"] = Text(Get(Main, "現場コード"));
        row["物件名"] = Text(Get(Main, "物件名"));
        var (pref, city, town) = SplitJapaneseAddress(Get(Main, "現場住所"));
        row["現場住所_都道府県"] = pref;
        row["現場住所_市区町村"] = city;
        row["現場住所_町名"] = town;

        foreach (var key in MainFields) row[key] = Get(Main, key);

        // 施工坪単価: シートに無ければ 請負金額 ÷ 施工坪（施工床面積 ÷ 3.305785）で求める
        var unitPrice = Get(Main, "施工坪");
        if (unitPrice is null && Number(row["請負金額"]) is double contract && Number(row["施工床面積"]) is double area && area != 0)
            unitPrice = contract / (area / M2PerTsubo);
        row["施工坪単価"] = unitPrice;

        row["工期(着工)"] = Get(Main, "工期(着工､竣工)");
        row["工期(竣工)"] = Get(Main, "工期(着工､竣工)_2");
        row["階数_地上"] = SafeInt(Get(Main, "階数_地上"));
        row["階数_地下"] = SafeInt(Get(Main, "階数_地下"));

        var counts = ParsePlanText(Get(Main, "間取"));
        foreach (var key in PlanKeys) row[OutputKeyOfPlan(key)] = counts[key];
        foreach (var key in CategoryKeys) row[key] = counts[key];
        row["間取"] = null;

        row["形態"] = Get(CostTrend, "形態");
        row["ELV"] = Get(CostTrend, "ELV");
        foreach (var key in HandoverFields) row[key] = Get(Handover, key);

        return row;
    }

    /// <summary>出力列名（1SLDK だけは見出しが全角の「１SLDK」）</summary>
    public static string OutputKeyOfPlan(string plan) => plan == "1SLDK" ? "１SLDK" : plan;

    // ── 補助（Python 版の to_nfk / safe_int / safe_number / split_japanese_address / parse_plan_text） ──

    public static string Nfkc(object? v) =>
        v is null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture)!.Normalize(NormalizationForm.FormKC).Trim();

    private static string? Text(object? v)
    {
        var s = v is null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "";
        return s.Length == 0 ? null : s;
    }

    /// <summary>整数化（"4階" → 4、空 → 0）</summary>
    public static int SafeInt(object? v)
    {
        if (v is null || (v is string s0 && s0.Trim().Length == 0)) return 0;
        if (v is double d) return (int)d;
        if (v is int i) return i;
        var text = Nfkc(v);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return (int)f;
        var m = Regex.Match(text, @"\d+");
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : 0;
    }

    public static double? Number(object? v)
    {
        if (v is null) return null;
        if (v is double d) return d;
        if (v is int i) return i;
        if (v is decimal m) return (double)m;
        var text = Nfkc(v).Replace(",", "");
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
    }

    private static readonly Regex PrefPattern = new(@"^(東京都|北海道|(?:京都|大阪)府|.{2,3}県)", RegexOptions.Compiled);

    /// <summary>住所 → (都道府県, 市区町村, 町名)。政令指定都市は「〇〇市〇〇区」までを市区町村とする。</summary>
    public static (string Pref, string City, string Town) SplitJapaneseAddress(object? address)
    {
        var text = Nfkc(address);
        if (text.Length == 0) return ("", "", "");

        var pm = PrefPattern.Match(text);
        var pref = pm.Success ? pm.Groups[1].Value : "";
        var rest = pref.Length > 0 ? text[pref.Length..] : text;

        var city = "";
        var m = Regex.Match(rest, @"^(.+?市.+?区)");
        if (m.Success) city = m.Groups[1].Value;
        else
        {
            m = Regex.Match(rest, @"^(.+?[市区町村])");
            if (m.Success) city = m.Groups[1].Value;
        }

        var tail = city.Length > 0 ? rest[city.Length..] : rest;
        var town = "";
        m = Regex.Match(tail, @"^(.+?\d+丁目)");
        if (m.Success) town = m.Groups[1].Value;
        else
        {
            m = Regex.Match(tail, @"^([^0-9\-－―ー]+)");
            if (m.Success) town = m.Groups[1].Value.Trim();
        }
        return (pref, city, town);
    }

    private static readonly Regex PlanPattern = new(
        @"((?:\d+|OR)(?:SLDK|LDK|SK|DK|R|K))\s*[-－ー]?\s*(\d+)\s*戸?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>間取の文字列 → 種類ごとの戸数（例: "1K-20戸、1LDK-5戸、店舗-1" → 1K=20, 1LDK=5, 店舗=1）</summary>
    public static Dictionary<string, int> ParsePlanText(object? planText)
    {
        var result = PlanKeys.Concat(CategoryKeys).ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        var text = Nfkc(planText);
        if (text.Length == 0) return result;
        var normalized = text.Replace("\r", "\n").Replace("\n", "、");

        foreach (Match m in PlanPattern.Matches(normalized))
        {
            var key = m.Groups[1].Value.ToUpperInvariant().Replace(" ", "");
            if (result.ContainsKey(key)) result[key] += int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        foreach (var cat in CategoryKeys)
        {
            foreach (Match m in Regex.Matches(normalized, Regex.Escape(cat) + @"\s*[-－ー]?\s*(\d+)", RegexOptions.IgnoreCase))
                result[cat] += int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return result;
    }
}
