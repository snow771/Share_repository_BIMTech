using System.Globalization;
using System.Text.Json;
using SimilaritySearchApp.Estimate.Models;

namespace SimilaritySearchApp.Estimate.Import;

/// <summary>
/// 構造化JSON（AI-OCR の「〜_structured.json」。キーは snake_case）を読む。
/// 数値は JSON の文字表現から decimal で読む（double を経由すると 0.1 などで桁がずれるため）。
/// 型が違う値（数値のはずが文字列など）は、黙って捨てずに、どの行・どの項目かを示して止める。
/// </summary>
public static class StructuredJsonReader
{
    public static StructuredPayload Read(byte[] utf8Json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(utf8Json, new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"構造化JSONを読み込めません（JSON の形式が正しくありません）: {e.Message}", e);
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("構造化JSONではありません（items がありません）。AI-OCR が出力した「〜_structured.json」を選んでください。");

            var p = new StructuredPayload();
            if (root.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object)
            {
                p.Metadata = new ImportMetadata
                {
                    CreatedAt = Str(m, "created_at"),
                    SourceLayoutJson = Str(m, "source_layout_json"),
                    CommonRulesFile = Str(m, "common_rules_file"),
                    SupplierRulesFile = Str(m, "supplier_rules_file"),
                    SupplierName = Str(m, "supplier_name"),
                    ProjectName = Str(m, "project_name"),
                    EstimateId = Str(m, "estimate_id"),
                };
            }
            if (root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                const string where = "summary";
                p.Summary = new ImportSummary
                {
                    DetailCount = Int(s, "detail_count", where) ?? 0,
                    MoneyDetailCount = Int(s, "money_detail_count", where) ?? 0,
                    ReferenceDetailCount = Int(s, "reference_detail_count", where) ?? 0,
                    AmountCheckOkCount = Int(s, "amount_check_ok_count", where) ?? 0,
                    DuplicateCandidateCount = Int(s, "duplicate_candidate_count", where) ?? 0,
                    HeaderEstimateAmount = Dec(s, "header_estimate_amount", where),
                    DetailAmountTotal = Dec(s, "detail_amount_total", where),
                    DifferenceToHeaderAmount = Dec(s, "difference_to_header_amount", where),
                    AuditRowCount = Int(s, "audit_row_count", where) ?? 0,
                    AuditExcludedCount = Int(s, "audit_excluded_count", where) ?? 0,
                };
            }

            var n = 0;
            foreach (var it in items.EnumerateArray())
            {
                var where = $"明細 {++n} 行目";
                p.Items.Add(new ImportItem
                {
                    RecordType = Str(it, "record_type"),
                    PdfPage = Int(it, "pdf_page", where),
                    OcrTableIndex = Int(it, "ocr_table_index", where),
                    OcrRowIndex = Int(it, "ocr_row_index", where),
                    Trade = Str(it, "trade"),
                    Section = Str(it, "section"),
                    ItemName = Str(it, "item_name"),
                    Material = Str(it, "material"),
                    Dimension = Str(it, "dimension"),
                    Quantity = Dec(it, "quantity", where),
                    Unit = Str(it, "unit"),
                    UnitPrice = Dec(it, "unit_price", where),
                    Amount = Dec(it, "amount", where),
                    RemarksOcr = Str(it, "remarks_ocr"),
                    CalculatedAmount = Dec(it, "calculated_amount", where),
                    Difference = Dec(it, "difference", where),
                    AmountCheck = Str(it, "amount_check"),
                    DuplicateCandidate = Str(it, "duplicate_candidate"),
                    ReviewRequired = Str(it, "review_required"),
                    ReviewReason = Str(it, "review_reason"),
                });
            }

            if (root.TryGetProperty("audit_rows", out var audit) && audit.ValueKind == JsonValueKind.Array)
            {
                n = 0;
                foreach (var a in audit.EnumerateArray())
                {
                    var where = $"OCR行監査 {++n} 行目";
                    p.AuditRows.Add(new ImportAuditRow
                    {
                        PdfPage = Int(a, "pdf_page", where),
                        OcrTableIndex = Int(a, "ocr_table_index", where),
                        OcrRowIndex = Int(a, "ocr_row_index", where),
                        RowStatus = Str(a, "row_status"),
                        Reason = Str(a, "reason"),
                        RowText = Str(a, "row_text"),
                    });
                }
            }
            return p;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => v.GetRawText(),
        } : "";

    /// <summary>数値を decimal で読む（null・空文字は null）。数として読めなければ止める。</summary>
    public static decimal? Dec(JsonElement e, string name, string where)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        switch (v.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return null;
            case JsonValueKind.Number:
                if (v.TryGetDecimal(out var d)) return d;
                // 1e-7 のような指数表記
                if (decimal.TryParse(v.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                break;
            case JsonValueKind.String when string.IsNullOrWhiteSpace(v.GetString()):
                return null;
            case JsonValueKind.String:
                if (decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                break;
        }
        throw new InvalidOperationException($"構造化JSONの {where} の「{name}」を数値として読めません: {v.GetRawText()}");
    }

    private static int? Int(JsonElement e, string name, string where)
    {
        var d = Dec(e, name, where);
        if (d is null) return null;
        if (d != decimal.Truncate(d.Value) || d < int.MinValue || d > int.MaxValue)
            throw new InvalidOperationException($"構造化JSONの {where} の「{name}」が整数ではありません: {d}");
        return (int)d.Value;
    }
}
