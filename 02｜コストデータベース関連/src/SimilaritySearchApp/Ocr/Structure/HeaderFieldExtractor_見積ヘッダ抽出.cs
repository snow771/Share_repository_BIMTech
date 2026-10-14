using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SimilaritySearchApp.Ocr.Structure;

/// <summary>
/// 見積ヘッダ（工事名・協力業者名・見積番号・見積金額）を取り出す（Python 版 extract_header_fields と同じ順序）。
///   1. OCR 行の「ラベル → 同じ行の続き／次の行」（帳票のヘッダは表ではなく行で返ることが多い）
///   2. 表の「ラベルのセル → 右隣のセル」
///   3. 本文に対する正規表現（補完。3 文字未満は採用しない）
///   4. 協力業者名が無ければ、会社名らしい行を探す
/// 自社名（exclude_supplier_patterns）は協力業者名として採用しない。
/// </summary>
public static class HeaderFieldExtractor
{
    private const string Legal = "(?:株式会社|有限会社|合同会社|合資会社|合名会社)";

    private static readonly Regex LeadingMark = new(@"^(?:[:：]|[-―－]+)\s*");
    private static readonly Regex TrailingHonorific = new(@"\s*(?:御中|様)\s*$");
    private static readonly Regex CompanyNoise = new($@"(?:株式会社|有限会社|合同会社|合資会社|合名会社|[\s=＝:：・,，.．])");
    private static readonly Regex LegalOnly = new($@"\A{Legal}\z");
    private static readonly Regex NotCompanyNext = new(@"^(?:御中|様|〒|TEL|FAX)$");
    private static readonly Regex LegalSuffix = new($@"([^\n〒]{{2,50}}?{Legal})\s*(?:御中|様)?$");
    private static readonly Regex LegalPrefix = new($@"({Legal}\s*[^\n〒御中様]{{2,50}})");

    public static Dictionary<string, string> Extract(JsonNode layout, OcrRules rules)
    {
        var fields = new Dictionary<string, string>();
        var lines = HeaderLines(layout);

        foreach (var (field, labels) in rules.TableLabels)
        {
            var value = FindLineField(lines, labels, field, rules);
            if (value.Length == 0) value = FindTableField(layout, labels, field, rules);
            if (value.Length > 0) fields[field] = value;
        }

        var content = (string?)layout["content"] ?? "";
        foreach (var (field, patterns) in rules.ContentPatterns)
        {
            if (fields.TryGetValue(field, out var got) && got.Length > 0) continue;
            foreach (var pattern in patterns)
            {
                foreach (Match m in Regex.Matches(content, pattern, RegexOptions.Multiline))
                {
                    var candidate = CleanHeaderValue(m.Groups.Count > 1 ? m.Groups[1].Value : m.Value);
                    if (candidate.Length < 3) continue;
                    if (field == "supplier_name")
                    {
                        candidate = CompanyFromLine(candidate, "", rules);
                        if (candidate.Length == 0 || IsExcludedSupplier(candidate, rules)) continue;
                    }
                    fields[field] = candidate;
                    break;
                }
                if (fields.TryGetValue(field, out var found) && found.Length > 0) break;
            }
        }

        if (!fields.TryGetValue("supplier_name", out var supplier) || supplier.Length == 0)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var candidate = CompanyFromLine(lines[i], i + 1 < lines.Count ? lines[i + 1] : "", rules);
                if (candidate.Length > 0)
                {
                    fields["supplier_name"] = candidate;
                    break;
                }
            }
        }
        return fields;
    }

    public static string CleanHeaderValue(string value)
    {
        value = OcrText.Clean(value);
        value = LeadingMark.Replace(value, "", 1);
        value = TrailingHonorific.Replace(value, "", 1);
        return value.Trim();
    }

    private static bool IsExcludedSupplier(string value, OcrRules rules) =>
        rules.ExcludeSupplierPatterns.Any(p => Regex.IsMatch(value, p, RegexOptions.IgnoreCase));

    private static List<string> HeaderLines(JsonNode layout)
    {
        var lines = new List<string>();
        foreach (var page in OcrText.Pages(layout))
        {
            foreach (var line in page["lines"] as JsonArray ?? new JsonArray())
            {
                var value = OcrText.Clean((string?)line?["content"]);
                if (value.Length > 0) lines.Add(value);
            }
        }
        return lines;
    }

    private static bool ValidCompanyCandidate(string value) => CompanyNoise.Replace(value, "").Length >= 2;

    /// <summary>1 行（または「株式会社」で改行された 2 行）から社名を取り出す。社名でなければ空。</summary>
    public static string CompanyFromLine(string line, string nextLine, OcrRules rules)
    {
        line = CleanHeaderValue(line);
        nextLine = CleanHeaderValue(nextLine);
        string Accept(string candidate) =>
            IsExcludedSupplier(candidate, rules) || !ValidCompanyCandidate(candidate) ? "" : candidate;

        // OCR が「株式会社」だけで改行しているケース
        if (LegalOnly.IsMatch(line))
        {
            return nextLine.Length > 0 && !NotCompanyNext.IsMatch(nextLine) ? Accept($"{line}{nextLine}") : "";
        }

        // 「中川インテリア株式会社」のような後置法人格を先に判定する
        var suffix = LegalSuffix.Match(line);
        if (suffix.Success) return Accept(CleanHeaderValue(suffix.Groups[1].Value));

        // 「株式会社八千代工業」のような前置法人格
        var prefix = LegalPrefix.Match(line);
        if (prefix.Success) return Accept(CleanHeaderValue(prefix.Groups[1].Value));

        return "";
    }

    private static string FindLineField(List<string> lines, List<string> labels, string field, OcrRules rules)
    {
        var normalizedLabels = labels.Select(OcrText.NormalizeLabel).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var norm = OcrText.NormalizeLabel(line);
            foreach (var label in normalizedLabels)
            {
                string candidate;
                if (norm == label)
                {
                    // ラベルだけの行 → 次の行が値
                    candidate = i + 1 < lines.Count ? CleanHeaderValue(lines[i + 1]) : "";
                }
                else if (label.Length > 0 && norm.StartsWith(label, StringComparison.Ordinal) && norm.Length > label.Length)
                {
                    // 例: 「件 名 文京区本郷4丁目計画」→ 空白を許してラベルを取り除く
                    var pattern = "^" + string.Concat(label.Select(ch => Regex.Escape(ch.ToString()) + @"\s*"));
                    candidate = CleanHeaderValue(Regex.Replace(line, pattern, ""));
                }
                else
                {
                    continue;
                }

                if (field == "supplier_name")
                {
                    var next = i + 1 < lines.Count ? lines[i + 1] : "";
                    var company = CompanyFromLine(candidate, next, rules);
                    if (company.Length > 0) candidate = company;
                    if (IsExcludedSupplier(candidate, rules)) continue;
                }
                if (candidate.Length > 0) return candidate;
            }
        }
        return "";
    }

    private static string FindTableField(JsonNode layout, List<string> labels, string field, OcrRules rules)
    {
        var normalizedLabels = labels.Select(OcrText.NormalizeLabel).ToHashSet();
        foreach (var table in OcrText.Tables(layout))
        {
            foreach (var row in OcrText.TableMatrix(table))
            {
                for (var i = 0; i < row.Length; i++)
                {
                    if (!normalizedLabels.Contains(OcrText.NormalizeLabel(row[i]))) continue;
                    foreach (var value in row.Skip(i + 1))
                    {
                        var candidate = CleanHeaderValue(value);
                        if (candidate.Length == 0) continue;
                        if (field == "supplier_name")
                        {
                            candidate = CompanyFromLine(candidate, "", rules);
                            if (candidate.Length == 0 || IsExcludedSupplier(candidate, rules)) continue;
                        }
                        return candidate;
                    }
                }
            }
        }
        return "";
    }
}
