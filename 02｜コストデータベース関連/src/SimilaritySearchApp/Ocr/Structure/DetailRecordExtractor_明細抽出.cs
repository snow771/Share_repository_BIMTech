using System.Globalization;
using System.Text.Json.Nodes;
using SimilaritySearchApp.Ocr.Models;

namespace SimilaritySearchApp.Ocr.Structure;

/// <summary>
/// 明細表を見つけて 1 行ずつ判定する（Python 版 v6 の find_detail_tables / extract_records / mark_duplicates）。
///   ・金額（数量・単価・金額がそろう）行 → 金額明細。名称だけ・数量だけの行も「参考明細」として残す（集計からは除外）
///   ・区分 （…）／工種の見出し行は以降の明細の 区分・工種 になる
///   ・名称なし・合計行は明細にしないが、すべての行を「OCR行監査」に 採用／見出し／除外／空行 として残す
/// </summary>
public static class DetailRecordExtractor
{
    public sealed record DetailTable(int TableIndex, JsonNode Table, List<string[]> Matrix, int HeaderIndex, Dictionary<string, int> Mapping);

    /// <summary>
    /// 見出し行の各セルがどの項目の列かを決める。
    /// 罫線が薄い帳票では「備考 数量」のように 2 つの見出しが 1 セルにまとまることがあるため、その場合は右隣の列を数量とする。
    /// </summary>
    public static Dictionary<string, int> FindColumnMap(string[] header, OcrRules rules)
    {
        var result = new Dictionary<string, int>();
        var normalized = header.Select(OcrText.NormalizeLabel).ToArray();
        bool Contains(string cell, string field) => rules.Aliases(field).Any(c => cell.Contains(OcrText.NormalizeLabel(c), StringComparison.Ordinal));

        for (var i = 0; i < normalized.Length; i++)
        {
            if (normalized[i].Length == 0) continue;
            foreach (var (field, candidates) in rules.ColumnAliases)
            {
                if (candidates.Any(c => normalized[i].Contains(OcrText.NormalizeLabel(c), StringComparison.Ordinal)))
                    result.TryAdd(field, i);   // 同じ項目が複数あれば左の列を優先
            }
        }

        // 例: ["記号","名称","規格・寸法","備考 数量","","単位","単価","金額"] → remarks=3, quantity=4
        for (var i = 0; i < normalized.Length; i++)
        {
            if (Contains(normalized[i], "remarks") && Contains(normalized[i], "quantity"))
            {
                result["remarks"] = i;
                if (i + 1 < header.Length) result["quantity"] = i + 1;
            }
        }

        // 数量と備考が同じ列になった場合は右隣を数量にする
        if (result.TryGetValue("remarks", out var r) && result.TryGetValue("quantity", out var q) && r == q && r + 1 < header.Length)
            result["quantity"] = r + 1;

        return result;
    }

    /// <summary>明細表（見出しに必須列がある表。名称＋数量だけの数量表も含む）を探す。</summary>
    public static List<DetailTable> FindDetailTables(JsonNode layout, OcrRules rules)
    {
        var found = new List<DetailTable>();
        var required = rules.RequiredColumns.ToHashSet();
        var index = 0;
        foreach (var table in OcrText.Tables(layout))
        {
            index++;
            var matrix = OcrText.TableMatrix(table);
            for (var h = 0; h < Math.Min(rules.HeaderSearchRows, matrix.Count); h++)
            {
                var mapping = FindColumnMap(matrix[h], rules);
                if (required.All(mapping.ContainsKey) || (mapping.ContainsKey("item_name") && mapping.ContainsKey("quantity")))
                {
                    found.Add(new DetailTable(index, table, matrix, h, mapping));
                    break;
                }
            }
        }
        return found;
    }

    private static bool IsTotalRow(string name)
    {
        var n = OcrText.NormalizeLabel(name);
        return n is "合計" or "小計" or "総計" or "計" || n.EndsWith("合計", StringComparison.Ordinal);
    }

    private static string Cell(string[] row, Dictionary<string, int> mapping, string field) =>
        mapping.TryGetValue(field, out var i) && i < row.Length ? OcrText.Clean(row[i]) : "";

    public static (List<EstimateItem> Items, List<AuditRow> Audit) ExtractRecords(
        JsonNode layout, OcrRules rules, Dictionary<string, string> header, string sourceName)
    {
        var items = new List<EstimateItem>();
        var audit = new List<AuditRow>();
        string currentTrade = "", currentSection = "";
        var ignore = rules.IgnoreValues.Select(OcrText.NormalizeLabel).ToHashSet();
        string H(string key) => header.TryGetValue(key, out var v) ? v : "";

        foreach (var t in FindDetailTables(layout, rules))
        {
            var page = OcrText.PageNumber(t.Table);
            for (var r = t.HeaderIndex + 1; r < t.Matrix.Count; r++)
            {
                var rowNo = r + 1;   // 表の何行目か（1 始まり。見出し行を含む）
                var row = t.Matrix[r].Select(OcrText.Clean).ToArray();
                var rowText = string.Join(" | ", row);
                void Audit(string status, string reason) => audit.Add(new AuditRow
                {
                    PdfPage = page, OcrTableIndex = t.TableIndex, OcrRowIndex = rowNo, RowStatus = status, Reason = reason, RowText = rowText,
                });

                var meaningful = row.Select(OcrText.NormalizeLabel).Where(v => v.Length > 0 && !ignore.Contains(v)).ToList();
                if (meaningful.Count == 0)
                {
                    Audit("空行", "");
                    continue;
                }

                var name = Cell(row, t.Mapping, "item_name");
                var material = Cell(row, t.Mapping, "material");
                var dimension = Cell(row, t.Mapping, "dimension");
                var quantity = OcrText.ToNumber(Cell(row, t.Mapping, "quantity"));
                var unitRaw = Cell(row, t.Mapping, "unit");
                var unit = rules.UnitAliases.TryGetValue(unitRaw, out var u) ? u : unitRaw;
                var unitPrice = OcrText.ToNumber(Cell(row, t.Mapping, "unit_price"));
                var amount = OcrText.ToNumber(Cell(row, t.Mapping, "amount"));
                var remarks = Cell(row, t.Mapping, "remarks");

                var numericBlank = quantity is null && unitPrice is null && amount is null;
                var otherBlank = material.Length == 0 && dimension.Length == 0 && unit.Length == 0 && remarks.Length == 0;

                // 見出し・区分行
                if (name.Length > 0 && numericBlank && otherBlank)
                {
                    if (OcrText.MatchesAny(name, rules.SectionPatterns))
                    {
                        currentSection = name.Trim('(', ')', '（', '）', ' ');
                        Audit("見出し", "区分見出し");
                        continue;
                    }
                    if (IsTotalRow(name))
                    {
                        Audit("除外", "合計行");
                        continue;
                    }
                    if (!OcrText.MatchesAny(name, rules.IgnoreNamePatterns))
                    {
                        currentTrade = name;
                        Audit("見出し", "工種・大見出し");
                        continue;
                    }
                }

                // 名称がない行は明細にしないが、監査には残す
                if (name.Length == 0 || IsTotalRow(name))
                {
                    Audit("除外", "名称なし、または合計行");
                    continue;
                }

                var isMoney = quantity is not null && unitPrice is not null && amount is not null;
                var recordType = isMoney ? "金額明細" : "参考明細";
                var reasons = new List<string>();
                long? calc = null, diff = null;
                var check = "対象外";
                if (isMoney)
                {
                    calc = OcrText.Round(quantity!.Value * unitPrice!.Value);
                    diff = OcrText.Round(amount!.Value - calc.Value);
                    check = Math.Abs(diff.Value) <= 1 ? "OK" : "要確認";
                    if (Math.Abs(diff.Value) > 1) reasons.Add("数量×単価と金額が不一致");
                }
                else
                {
                    reasons.Add("単価・金額が未記載の参考明細（集計対象外）");
                }
                if (remarks.Length > 0 && OcrText.MatchesAny(remarks, rules.RemarkReviewPatterns)) reasons.Add("備考OCRに誤認識候補");

                items.Add(new EstimateItem
                {
                    EstimateId = H("estimate_id"),
                    SupplierName = H("supplier_name"),
                    ProjectName = H("project_name"),
                    SourceFile = sourceName,
                    RecordType = recordType,
                    PdfPage = page,
                    OcrTableIndex = t.TableIndex,
                    OcrRowIndex = rowNo,
                    Trade = currentTrade,
                    Section = currentSection,
                    ItemName = name,
                    Material = material,
                    Dimension = dimension,
                    Quantity = quantity,
                    Unit = unit,
                    UnitPrice = unitPrice,
                    Amount = amount,
                    RemarksOcr = remarks,
                    CalculatedAmount = calc,
                    Difference = diff,
                    AmountCheck = check,
                    ReviewRequired = reasons.Count > 0 ? "要確認" : "",
                    ReviewReason = string.Join(" / ", reasons),
                });
                Audit("採用", recordType);
            }
        }
        return (items, audit);
    }

    /// <summary>
    /// ページをまたいで同じ明細が重複していないかの候補を付ける。
    ///   同一明細候補: 名称・材料・寸法・数量・単位・単価・金額がすべて同じで、別ページにある
    ///   近似重複候補: 寸法以外が同じで、別ページにあり、寸法が空または食い違う（ページ継続時のセル欠落）
    /// </summary>
    public static void MarkDuplicates(List<EstimateItem> items)
    {
        string N(double? v) => v?.ToString("R", CultureInfo.InvariantCulture) ?? "∅";
        string ExactKey(EstimateItem r) => string.Join("\u0001", r.ItemName, r.Material, r.Dimension, N(r.Quantity), r.Unit, N(r.UnitPrice), N(r.Amount));
        string NearKey(EstimateItem r) => string.Join("\u0001", r.ItemName, r.Material, N(r.Quantity), r.Unit, N(r.UnitPrice), N(r.Amount));

        void Flag(List<int> indexes, string label, string reason)
        {
            foreach (var i in indexes)
            {
                var rec = items[i];
                rec.DuplicateCandidate = label;
                rec.ReviewRequired = "要確認";
                rec.ReviewReason = $"{rec.ReviewReason} / {reason}".Trim(' ', '/');
            }
        }

        var exact = Group(items, ExactKey);
        var near = Group(items, NearKey);

        foreach (var indexes in exact)
        {
            var pages = indexes.Select(i => items[i].PdfPage).Distinct().Count();
            if (indexes.Count > 1 && pages > 1) Flag(indexes, "同一明細候補", "ページ継続・重複を確認");
        }
        foreach (var indexes in near)
        {
            var pages = indexes.Select(i => items[i].PdfPage).Distinct().Count();
            var dims = indexes.Select(i => OcrText.Clean(items[i].Dimension)).ToHashSet();
            if (indexes.Count > 1 && pages > 1 && (dims.Contains("") || dims.Count > 1)
                && indexes.Any(i => items[i].DuplicateCandidate.Length == 0))
            {
                Flag(indexes, "近似重複候補", "ページ継続時のセル欠落・重複を確認");
            }
        }
    }

    /// <summary>キーごとの行番号（最初に現れた順）。</summary>
    private static List<List<int>> Group(List<EstimateItem> items, Func<EstimateItem, string> key)
    {
        var order = new List<string>();
        var map = new Dictionary<string, List<int>>();
        for (var i = 0; i < items.Count; i++)
        {
            var k = key(items[i]);
            if (!map.TryGetValue(k, out var list))
            {
                map[k] = list = new List<int>();
                order.Add(k);
            }
            list.Add(i);
        }
        return order.Select(k => map[k]).ToList();
    }
}
