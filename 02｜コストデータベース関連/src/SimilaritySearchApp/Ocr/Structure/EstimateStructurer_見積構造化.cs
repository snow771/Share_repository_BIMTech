using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimilaritySearchApp.Ocr.Models;
using SimilaritySearchApp.Ocr.Output;

namespace SimilaritySearchApp.Ocr.Structure;

/// <summary>
/// Layout JSON（全ページ統合済み）を構造化明細にする（Python 版 estimate_ocr_structured_v6 の main）。
/// Azure には送らない。OCR 済みの JSON だけを読むので、ルールを直して何度やり直しても利用枚数は増えない。
/// </summary>
public static class EstimateStructurer
{
    /// <summary>構造化 JSON の書式（Python 版と同じ snake_case・日本語はそのまま）。</summary>
    public static JsonSerializerOptions SnakeJson { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static StructuredResult Structure(JsonNode layout, OcrRules rules, string sourceName, string commonRulesName, string? supplierRulesName, DateTime? now = null)
    {
        if (DetailRecordExtractor.FindDetailTables(layout, rules).Count == 0)
            throw new InvalidOperationException("明細表を特定できませんでした。共通ルールまたは協力業者差分ルールの列名候補（column_aliases）を確認してください。");

        var header = HeaderFieldExtractor.Extract(layout, rules);
        var (items, audit) = DetailRecordExtractor.ExtractRecords(layout, rules, header, sourceName);
        DetailRecordExtractor.MarkDuplicates(items);
        return MakeResult(items, audit, header, sourceName, commonRulesName, supplierRulesName, now ?? DateTime.Now);
    }

    public static StructuredResult MakeResult(
        List<EstimateItem> items, List<AuditRow> audit, Dictionary<string, string> header,
        string sourceName, string commonRulesName, string? supplierRulesName, DateTime now)
    {
        string H(string key) => header.TryGetValue(key, out var v) ? v : "";
        var headerTotal = OcrText.ToNumber(H("header_estimate_amount"));
        var money = items.Where(r => r.RecordType == "金額明細").ToList();
        var total = money.Sum(r => r.Amount ?? 0);
        return new StructuredResult
        {
            Metadata = new StructuredMetadata
            {
                CreatedAt = now.ToString("yyyy-MM-ddTHH:mm:ss"),
                SourceLayoutJson = sourceName,
                CommonRulesFile = commonRulesName,
                SupplierRulesFile = supplierRulesName ?? "",
                SupplierName = H("supplier_name"),
                ProjectName = H("project_name"),
                EstimateId = H("estimate_id"),
            },
            Summary = new StructuredSummary
            {
                DetailCount = items.Count,
                MoneyDetailCount = money.Count,
                ReferenceDetailCount = items.Count(r => r.RecordType == "参考明細"),
                AmountCheckOkCount = money.Count(r => r.AmountCheck == "OK"),
                DuplicateCandidateCount = items.Count(r => r.DuplicateCandidate.Length > 0),
                HeaderEstimateAmount = headerTotal,
                DetailAmountTotal = total,
                DifferenceToHeaderAmount = headerTotal is double h ? OcrText.Round(h - total) : null,
                AuditRowCount = audit.Count,
                AuditExcludedCount = audit.Count(r => r.RowStatus == "除外"),
                ReviewRequiredCount = items.Count(r => r.ReviewRequired.Length > 0),
            },
            Items = items,
            AuditRows = audit,
        };
    }

    /// <summary>
    /// Layout JSON ファイルを構造化し、JSON と Excel を outputDir に保存する。
    /// 保存名は「（Layout JSON の名前から _layout_raw を除いたもの）_structured.json / .xlsx」。
    /// </summary>
    public static (StructuredResult Result, string JsonPath, string ExcelPath) StructureFile(
        string layoutJsonPath, string commonRulesPath, string? supplierRulesPath, string outputDir)
    {
        if (!File.Exists(layoutJsonPath)) throw new FileNotFoundException($"OCR の JSON が見つかりません: {layoutJsonPath}");
        var layout = JsonNode.Parse(File.ReadAllText(layoutJsonPath, Encoding.UTF8))
                     ?? throw new InvalidOperationException("OCR の JSON が空です。");
        var rules = OcrRules.Load(commonRulesPath, supplierRulesPath);
        var result = Structure(layout, rules, Path.GetFileName(layoutJsonPath), Path.GetFileName(commonRulesPath),
            string.IsNullOrWhiteSpace(supplierRulesPath) ? null : Path.GetFileName(supplierRulesPath));

        Directory.CreateDirectory(outputDir);
        var stem = Path.GetFileNameWithoutExtension(layoutJsonPath).Replace("_layout_raw", "");
        var jsonPath = Path.Combine(outputDir, $"{stem}_structured.json");
        var excelPath = Path.Combine(outputDir, $"{stem}_structured.xlsx");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(result, SnakeJson), new UTF8Encoding(false));
        StructuredExcelWriter.Write(result, excelPath);
        return (result, jsonPath, excelPath);
    }
}
