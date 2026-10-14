using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Ocr.Azure;
using SimilaritySearchApp.Ocr.Models;
using SimilaritySearchApp.Ocr.Output;
using SimilaritySearchApp.Ocr.Pdf;
using SimilaritySearchApp.Ocr.Structure;
using SimilaritySearchApp.Ocr.Usage;

namespace SimilaritySearchApp.Ocr;

/// <summary>実行の指定（画面の入力）。</summary>
public sealed class OcrRunOptions
{
    public List<string> Pdfs { get; set; } = new();
    public string OutputFolder { get; set; } = "";
    public string CommonRulesPath { get; set; } = "";
    public string? SupplierRulesPath { get; set; }
    public bool DoStructure { get; set; } = true;
    public bool KeepRawJson { get; set; } = true;
    public bool KeepRawExcel { get; set; } = true;
    public bool KeepChunks { get; set; }
}

/// <summary>
/// PDF ごとに「2 ページずつ分割 → Azure で OCR → 全ページを 1 つに統合 → 構造化」を行う（Python 版 GUI の run_batch）。
///
/// 出力（PDF ごとにフォルダを作る）:
///   {保存先}\{PDF名}_{日時}\
///     01_OCR抽出\{PDF名}_{日時}_layout_raw_full.json   … Azure の結果（全ページ統合）
///     01_OCR抽出\{PDF名}_{日時}_layout_check_full.xlsx … OCR 確認用 Excel
///     02_構造化\{PDF名}_{日時}_full_structured.json / .xlsx
///     00_ページ数警告.txt                              … PDF と Azure のページ数が合わないとき
///
/// 1 つの PDF が失敗しても次の PDF は続ける。中止したときは、処理中の PDF までで止める。
/// 利用枚数は、Azure が解析を終えたページ数を 1 ブロックごとに数える。
/// </summary>
public sealed class OcrJobRunner
{
    public const string RawFolderName = "01_OCR抽出";
    public const string StructuredFolderName = "02_構造化";

    private static readonly JsonSerializerOptions RawJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly OcrSettings _settings;
    private readonly OcrUsageCounter _counter;
    private readonly Action<string, string, int, int> _progress;
    private readonly Action _usageChanged;

    /// <param name="progress">(level, message, done, total)。level: step / file / ok / warn / error / done</param>
    /// <param name="usageChanged">利用枚数が増えたとき（画面のメーターを更新）</param>
    public OcrJobRunner(OcrSettings settings, OcrUsageCounter counter, Action<string, string, int, int> progress, Action usageChanged)
    {
        _settings = settings;
        _counter = counter;
        _progress = progress;
        _usageChanged = usageChanged;
    }

    public async Task<List<OcrJobResult>> RunAsync(OcrRunOptions o, CancellationToken ct)
    {
        Validate(o);
        using var client = AzureLayoutClient.FromSettings(_settings);
        var chunkPages = Math.Max(1, _settings.ChunkPages);

        // ページ数を先に数えて、進捗の分母（ブロック数）にする
        var pageCounts = o.Pdfs.Select(p =>
        {
            try
            {
                return (int?)PdfSplitter.CountPages(p);
            }
            catch (Exception)
            {
                return null;
            }
        }).ToList();
        var total = pageCounts.Sum(n => n is int p ? (p + chunkPages - 1) / chunkPages : 1);
        var done = 0;
        Directory.CreateDirectory(o.OutputFolder);

        var results = new List<OcrJobResult>();
        for (var n = 0; n < o.Pdfs.Count; n++)
        {
            var pdf = o.Pdfs[n];
            var job = new OcrJobResult { Index = n, PdfPath = pdf, PdfName = Path.GetFileName(pdf), PdfPages = pageCounts[n] };
            results.Add(job);
            if (ct.IsCancellationRequested)
            {
                job.Status = "canceled";
                job.Error = "中止したため処理していません。";
                continue;
            }

            _progress("step", $"[{n + 1}/{o.Pdfs.Count}] {job.PdfName}", done, total);
            try
            {
                done = await RunOneAsync(client, job, o, chunkPages, done, total, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                job.Status = "canceled";
                job.Error = "中止しました（このPDFの途中までの OCR 結果は保存していません）。";
                _progress("warn", $"  {job.Error}", done, total);
            }
            catch (Exception e)
            {
                job.Status = "error";
                job.Error = e.Message;
                _progress("error", $"  失敗: {e.Message}", done, total);
            }
        }

        var ok = results.Count(r => r.Status is "ok" or "warn");
        _progress("done", $"完了: 成功 {ok} 件 / 失敗 {results.Count(r => r.Status == "error")} 件 / ページ警告 {results.Count(r => r.Status == "warn")} 件"
                          + (results.Any(r => r.Status == "canceled") ? $" / 中止 {results.Count(r => r.Status == "canceled")} 件" : ""), total, total);
        return results;
    }

    private static void Validate(OcrRunOptions o)
    {
        if (o.Pdfs.Count == 0) throw new InvalidOperationException("PDF を選択してください。");
        if (string.IsNullOrWhiteSpace(o.OutputFolder)) throw new InvalidOperationException("保存先フォルダを選択してください。");
        if (o.DoStructure && !File.Exists(o.CommonRulesPath))
            throw new InvalidOperationException("構造化する場合は、共通ルール JSON を選択してください。");
        if (o.DoStructure && !string.IsNullOrWhiteSpace(o.SupplierRulesPath) && !File.Exists(o.SupplierRulesPath))
            throw new InvalidOperationException($"協力業者差分ルール JSON が見つかりません: {o.SupplierRulesPath}");
        var missing = o.Pdfs.Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"PDF が見つかりません: {string.Join(", ", missing.Select(Path.GetFileName))}");
    }

    private async Task<int> RunOneAsync(AzureLayoutClient client, OcrJobResult job, OcrRunOptions o, int chunkPages, int done, int total, CancellationToken ct)
    {
        var stem = Path.GetFileNameWithoutExtension(job.PdfPath);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var jobDir = Path.Combine(o.OutputFolder, $"{stem}_{stamp}");
        var rawDir = Path.Combine(jobDir, RawFolderName);
        Directory.CreateDirectory(rawDir);
        job.JobFolder = jobDir;

        // ── 1. 分割して OCR ──
        var totalPages = job.PdfPages ?? PdfSplitter.CountPages(job.PdfPath);
        var chunks = new List<LayoutMerger.ChunkResult>();
        foreach (var chunk in PdfSplitter.SplitAll(job.PdfPath, chunkPages))
        {
            ct.ThrowIfCancellationRequested();
            if (o.KeepChunks)
            {
                var keepDir = Path.Combine(rawDir, "_chunks");
                Directory.CreateDirectory(keepDir);
                await File.WriteAllBytesAsync(Path.Combine(keepDir, $"{stem}_p{chunk.StartPage:000}-{chunk.EndPage:000}.pdf"), chunk.Bytes, ct);
            }
            _progress("file", $"  OCR処理中: {chunk.Index}ブロック目（元PDF {chunk.StartPage}-{chunk.EndPage}ページ / 全{totalPages}ページ）", done, total);
            var result = await client.AnalyzeLayoutAsync(chunk.Bytes, ct);

            var actual = (result["pages"] as JsonArray)?.Count ?? 0;
            _counter.AddPages(actual);
            job.PagesCounted += actual;
            _usageChanged();
            if (actual != chunk.PageCount)
                _progress("warn", $"  {chunk.StartPage}-{chunk.EndPage}ページを送信しましたが、Azure は {actual} ページを返しました。", done, total);
            chunks.Add(new LayoutMerger.ChunkResult(chunk.Index, chunk.StartPage, chunk.EndPage, result));
            done++;
        }

        // ── 2. 全ページを統合して保存 ──
        var merged = LayoutMerger.Merge(chunks, job.PdfName, totalPages, chunkPages);
        var rawJson = Path.Combine(rawDir, $"{stem}_{stamp}_layout_raw_full.json");
        var checkXlsx = Path.Combine(rawDir, $"{stem}_{stamp}_layout_check_full.xlsx");
        await File.WriteAllTextAsync(rawJson, merged.ToJsonString(RawJson), new UTF8Encoding(false), ct);
        OcrCheckExcelWriter.Write(merged, checkXlsx);
        _counter.AddFile();
        _usageChanged();
        job.RawJsonPath = rawJson;
        job.CheckExcelPath = checkXlsx;

        // ── 3. ページ数の照合 ──
        job.OcrPages = (merged["pages"] as JsonArray)?.Count ?? 0;
        _progress("step", $"  ページ照合: PDF {job.PdfPages?.ToString() ?? "不明"}ページ / Azure取得 {job.OcrPages}ページ", done, total);
        if (job.PdfPages is int pp && pp == job.OcrPages)
        {
            _progress("ok", "  ページ照合: OK（PDF全ページを統合済み）", done, total);
        }
        else
        {
            job.Status = "warn";
            job.Warnings.Add($"PDF {job.PdfPages?.ToString() ?? "不明"} ページのうち、Azure から取得できたのは {job.OcrPages} ページです。");
            await File.WriteAllTextAsync(Path.Combine(jobDir, "00_ページ数警告.txt"),
                $"PDFページ数: {job.PdfPages?.ToString() ?? "不明"}\nAzure取得ページ数: {job.OcrPages}\n", new UTF8Encoding(true), ct);
            _progress("warn", "  【警告】PDF全ページが取得できていない可能性があります。", done, total);
        }

        // ── 4. 構造化 ──
        if (o.DoStructure)
        {
            _progress("step", "  構造化を開始...", done, total);
            var (structured, sJson, sXlsx) = EstimateStructurer.StructureFile(
                rawJson, o.CommonRulesPath, o.SupplierRulesPath, Path.Combine(jobDir, StructuredFolderName));
            job.Structured = structured;
            job.StructuredJsonPath = sJson;
            job.StructuredExcelPath = sXlsx;
            var s = structured.Summary;
            _progress("ok", $"  構造化: 明細 {s.DetailCount} 件（金額 {s.MoneyDetailCount} / 参考 {s.ReferenceDetailCount}）・検算OK {s.AmountCheckOkCount}・重複候補 {s.DuplicateCandidateCount}", done, total);
        }

        // ── 5. 残さない指定のファイルを消す ──
        if (!o.KeepRawJson && o.DoStructure)
        {
            File.Delete(rawJson);
            job.RawJsonPath = null;
        }
        if (!o.KeepRawExcel)
        {
            File.Delete(checkXlsx);
            job.CheckExcelPath = null;
        }

        _progress("ok", $"  完了: {jobDir}", done, total);
        return done;
    }

    /// <summary>
    /// OCR 済みの Layout JSON から構造化だけやり直す（Azure は使わない）。
    /// 保存先は JSON が「01_OCR抽出」にあれば隣の「02_構造化」、それ以外は JSON と同じ場所の「構造化出力」。
    /// </summary>
    public static OcrJobResult Restructure(string rawJsonPath, string commonRulesPath, string? supplierRulesPath)
    {
        var dir = Path.GetDirectoryName(rawJsonPath) ?? "";
        var outDir = Path.GetFileName(dir) == RawFolderName
            ? Path.Combine(Path.GetDirectoryName(dir) ?? dir, StructuredFolderName)
            : Path.Combine(dir, "構造化出力");
        var (result, json, xlsx) = EstimateStructurer.StructureFile(rawJsonPath, commonRulesPath, supplierRulesPath, outDir);
        return new OcrJobResult
        {
            PdfPath = rawJsonPath,
            PdfName = Path.GetFileName(rawJsonPath),
            JobFolder = Path.GetFileName(dir) == RawFolderName ? Path.GetDirectoryName(dir) : dir,
            RawJsonPath = rawJsonPath,
            StructuredJsonPath = json,
            StructuredExcelPath = xlsx,
            Structured = result,
        };
    }
}
