using System.IO;
using System.Security.Cryptography;
using System.Text;
using SimilaritySearchApp.Estimate.Data;
using SimilaritySearchApp.Estimate.Models;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Estimate.Import;

/// <summary>
/// AI-OCR の結果（構造化JSON・構造化Excel・統合OCR JSON・元PDF）を DB へ取り込む。Azure は使わない。
///   ・構造化JSON を明細の正本にする（Excel だけの取込は対象外）
///   ・構造化Excel・統合OCR JSON は、指定が無ければ AI-OCR の出力フォルダ構成から探す
///       {PDF名_日時}\02_構造化\〜_structured.json / .xlsx、{PDF名_日時}\01_OCR抽出\〜_layout_raw_full.json
///   ・取込識別キー ＝ SHA-256("v1|構造化JSONのSHA-256|ルール版")。同じ内容の再取込は既存の見積を返す
/// </summary>
public sealed class EstimateImportService
{
    private readonly EstimateRepository _repo;

    public EstimateImportService(DbSettings db) => _repo = new EstimateRepository(db);

    public EstimateImportService(EstimateRepository repo) => _repo = repo;

    /// <summary>取込前の確認（何を・どれだけ登録するか、既に登録済みか）。DB には書かない。</summary>
    public async Task<ImportPreview> PreviewAsync(ImportRequest req, CancellationToken ct = default)
    {
        var (payload, files, key, _) = Prepare(req);
        int? existing;
        await using (var conn = await _repo.OpenAsync(ct)) existing = await _repo.FindByIdentityAsync(conn, null, key, ct);

        var warnings = new List<string>();
        if (files.Pdf is null) warnings.Add("元PDFが指定されていません。下書き・確認はできますが、取決めの確定には元PDFが必要です（あとから追加できます）。");
        if (files.StructuredExcel is null) warnings.Add("構造化Excel が見つかりません（構造化JSON だけを取り込みます）。");
        if (payload.Items.Count != payload.Summary.DetailCount && payload.Summary.DetailCount > 0)
            warnings.Add($"明細の件数（{payload.Items.Count}）が集計（{payload.Summary.DetailCount}）と一致しません。");
        if (existing is not null) warnings.Insert(0, $"この構造化結果は登録済みです（見積 #{existing}）。取り込まずに、登録済みの見積を開きます。");

        var list = new List<ImportFileView>
        {
            View(EstimateDb.DocStructuredJson, "構造化JSON（明細の正本）", files.StructuredJsonPath, files.StructuredJson, true),
            View(EstimateDb.DocStructuredExcel, "構造化Excel", files.StructuredExcelPath, files.StructuredExcel, false),
            View(EstimateDb.DocRawJson, "統合OCR JSON（再構造化用）", files.RawJsonPath, files.RawJson, false),
            View(EstimateDb.DocOriginalPdf, "元PDF", files.PdfPath, files.Pdf, false),
        };
        var resolved = new ImportRequest
        {
            StructuredJsonPath = files.StructuredJsonPath, StructuredExcelPath = files.StructuredExcelPath,
            RawJsonPath = files.RawJsonPath, PdfPath = files.PdfPath, IsTestData = req.IsTestData,
        };
        return new ImportPreview(resolved, list, payload.Metadata, payload.Summary, payload.Items.Count, payload.AuditRows.Count, existing, warnings);
    }

    public async Task<ImportResult> ImportAsync(ImportRequest req, CancellationToken ct = default)
    {
        var (payload, files, key, rule) = Prepare(req);
        return await _repo.ImportAsync(payload, files, key, rule, req.IsTestData, ct);
    }

    private static ImportFileView View(string type, string label, string? path, DocumentRepository.StoredFile? f, bool required) =>
        new(type, label, path, f?.Content.LongLength, required);

    private static (StructuredPayload Payload, EstimateRepository.ImportFiles Files, string Key, string Rule) Prepare(ImportRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.StructuredJsonPath) || !File.Exists(req.StructuredJsonPath))
            throw new InvalidOperationException("構造化JSON（〜_structured.json）を選んでください。");

        var json = DocumentRepository.Load(EstimateDb.DocStructuredJson, req.StructuredJsonPath);
        var payload = StructuredJsonReader.Read(json.Content);

        var excelPath = Existing(req.StructuredExcelPath) ?? Path.ChangeExtension(req.StructuredJsonPath, ".xlsx");
        var rawPath = Existing(req.RawJsonPath) ?? FindRawJson(req.StructuredJsonPath, payload.Metadata.SourceLayoutJson);
        var pdfPath = Existing(req.PdfPath);
        if (!string.IsNullOrWhiteSpace(req.PdfPath) && pdfPath is null) throw new InvalidOperationException($"元PDFが見つかりません: {req.PdfPath}");
        if (pdfPath is not null && !pdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("元PDFには .pdf ファイルを選んでください。");

        var files = new EstimateRepository.ImportFiles(
            json,
            File.Exists(excelPath) ? DocumentRepository.Load(EstimateDb.DocStructuredExcel, excelPath) : null,
            rawPath is not null ? DocumentRepository.Load(EstimateDb.DocRawJson, rawPath) : null,
            pdfPath is not null ? DocumentRepository.Load(EstimateDb.DocOriginalPdf, pdfPath) : null,
            req.StructuredJsonPath, File.Exists(excelPath) ? excelPath : null, rawPath, pdfPath);

        var rule = $"common:{payload.Metadata.CommonRulesFile}|supplier:{payload.Metadata.SupplierRulesFile}";
        var key = IdentityKey(json.Hash, rule);
        return (payload, files, key, rule);
    }

    /// <summary>取込識別キー（64 桁の 16 進）。</summary>
    public static string IdentityKey(byte[] structuredJsonHash, string ruleVersion) =>
        DocumentRepository.Hex(SHA256.HashData(Encoding.UTF8.GetBytes($"v1|{DocumentRepository.Hex(structuredJsonHash)}|{ruleVersion}")));

    private static string? Existing(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;

    /// <summary>AI-OCR の出力フォルダ構成から統合OCR JSON を探す（02_構造化 の隣の 01_OCR抽出）。</summary>
    private static string? FindRawJson(string structuredJsonPath, string sourceLayoutJson)
    {
        var dir = Path.GetDirectoryName(structuredJsonPath);
        if (dir is null) return null;
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceLayoutJson))
        {
            candidates.Add(Path.Combine(dir, sourceLayoutJson));
            var parent = Path.GetDirectoryName(dir);
            if (parent is not null) candidates.Add(Path.Combine(parent, "01_OCR抽出", sourceLayoutJson));
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
