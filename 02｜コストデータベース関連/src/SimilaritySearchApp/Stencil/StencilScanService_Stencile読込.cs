using System.IO;
using SimilaritySearchApp.Excel;

namespace SimilaritySearchApp.Stencil;

/// <summary>読込の条件。</summary>
public sealed class StencilScanRequest
{
    public string SourceFolder { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;

    /// <summary>① 物件諸情報を抽出する</summary>
    public bool DoProjectInfo { get; set; } = true;

    /// <summary>② 査定・修正単価を集約する（テンプレート必須）</summary>
    public bool DoUnitPrice { get; set; } = true;

    public string? MappingPath { get; set; }
    public string? TemplatePath { get; set; }
}

/// <summary>Stencile 1ファイル分の読込結果。</summary>
public sealed class StencilFileResult
{
    public int Index { get; set; }
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }

    /// <summary>① の出力1行（列名 → 値）。① を行わない・失敗した場合は null。</summary>
    public Dictionary<string, object?>? ProjectInfo { get; set; }

    /// <summary>② の行（テンプレートの工種コード数ぶん）。画面には送らない（件数だけ送る）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<UnitPriceRow>? UnitPrices { get; set; }

    /// <summary>② で積算予算に見つかった工種コードの数 / テンプレートの工種コード数</summary>
    public int UnitPriceMatched { get; set; }
    public int UnitPriceTotal { get; set; }

    public List<string> Warnings { get; set; } = new();

    /// <summary>読込エラー（このファイルは登録・出力の対象外）</summary>
    public string? Error { get; set; }

    // ── DB との照合結果（読込後に設定） ──
    public bool ExistsInDb { get; set; }
    public int ExistingAssessRows { get; set; }
    public int ExistingReviseRows { get; set; }

    public bool IsValid => Error is null && !string.IsNullOrWhiteSpace(ProjectId);
}

public sealed class StencilScanResult
{
    public string SourceFolder { get; set; } = "";
    public string? MappingPath { get; set; }
    public string? TemplatePath { get; set; }
    public int TemplateCodeCount { get; set; }
    public bool DoProjectInfo { get; set; }
    public bool DoUnitPrice { get; set; }
    public List<StencilFileResult> Files { get; set; } = new();

    /// <summary>DB との照合ができたか（接続できなければ false。登録は不可だが Excel 出力はできる）</summary>
    public bool DbChecked { get; set; }
    public string? DbMessage { get; set; }
}

/// <summary>進捗の通知（ログの色分け: step / file / ok / warn / error）。</summary>
public delegate void StencilProgress(string level, string message, int done, int total);

/// <summary>Stencile フォルダを読み込み、① 物件諸情報 と ② 査定・修正単価 を抽出する。</summary>
public static class StencilScanService
{
    /// <summary>出力ファイル名（読込対象から除く）</summary>
    public static readonly string[] OutputFileNames = { "物件諸情報一覧.xlsx", "単価テーブル_集約結果.xlsx", "査定・修正単価出力_集約結果.xlsx" };

    public static List<string> CollectFiles(string folder, bool includeSubfolders)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"フォルダが見つかりません: {folder}");
        var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(folder, "*.*", option)
            .Where(p => p.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            .Where(p => !Path.GetFileName(p).StartsWith("~$", StringComparison.Ordinal))
            .Where(p => !OutputFileNames.Contains(Path.GetFileName(p)))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static StencilScanResult Scan(StencilScanRequest req, StencilProgress progress, CancellationToken ct = default)
    {
        if (!req.DoProjectInfo && !req.DoUnitPrice) throw new InvalidOperationException("①物件諸情報・②査定/修正単価のどちらかを選んでください。");

        var result = new StencilScanResult { SourceFolder = req.SourceFolder, DoProjectInfo = req.DoProjectInfo, DoUnitPrice = req.DoUnitPrice };

        progress("step", "マッピング YAML を読み込んでいます...", 0, 0);
        var mappingPath = StencilMapping.Find(req.MappingPath, req.SourceFolder)
            ?? throw new FileNotFoundException("マッピング YAML が見つかりません。設定してください。");
        var mapping = StencilMapping.Load(mappingPath);
        result.MappingPath = mappingPath;
        progress("ok", $"マッピング: {Path.GetFileName(mappingPath)}（{mapping.Sections.Count} セクション）", 0, 0);

        List<UnitPriceTemplateRow>? template = null;
        if (req.DoUnitPrice)
        {
            if (string.IsNullOrWhiteSpace(req.TemplatePath) || !File.Exists(req.TemplatePath))
                throw new FileNotFoundException("②の単価テンプレート（〇〇期RC造_〇〇単価.xlsx）を選んでください。");
            progress("step", "単価テンプレートを読み込んでいます...", 0, 0);
            template = UnitPriceAggregator.ReadTemplate(req.TemplatePath);
            result.TemplatePath = req.TemplatePath;
            result.TemplateCodeCount = template.Count;
            progress("ok", $"テンプレート: {Path.GetFileName(req.TemplatePath)}（工種コード {template.Count} 件）", 0, 0);
        }

        var files = CollectFiles(req.SourceFolder, req.IncludeSubfolders);
        if (files.Count == 0) throw new FileNotFoundException("フォルダに Excel ファイル（.xlsm / .xlsx）がありません。");
        progress("step", $"{files.Count} 件のファイルを読み込みます。", 0, files.Count);

        var sheets = mapping.SheetNames.Concat(new[] { UnitPriceAggregator.EstimateSheet, UnitPriceAggregator.MainSheet }).Distinct().ToList();
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = files[i];
            var item = new StencilFileResult { Index = i, FileName = Path.GetRelativePath(req.SourceFolder, path), FilePath = path };
            result.Files.Add(item);
            progress("file", item.FileName, i, files.Count);
            try
            {
                var book = ExcelWorkbookReader.Read(path, sheets);
                item.ProjectId = ReadText(mapping.Read(book, "実行予算表書", "現場コード"))
                                 ?? ReadText(book.TryGetValue(UnitPriceAggregator.MainSheet, out var main) ? main.Get("C1") : null);
                item.ProjectName = ReadText(mapping.Read(book, "実行予算表書", "物件名"));

                if (req.DoProjectInfo)
                {
                    try
                    {
                        item.ProjectInfo = ProjectInfoExtractor.Extract(book, mapping);
                    }
                    catch (Exception e)
                    {
                        item.Warnings.Add("① 物件諸情報: " + e.Message);
                    }
                }
                if (req.DoUnitPrice && template is not null)
                {
                    try
                    {
                        item.UnitPrices = UnitPriceAggregator.Extract(book, template, item.ProjectId);
                        item.UnitPriceTotal = item.UnitPrices.Count;
                        item.UnitPriceMatched = item.UnitPrices.Count(u => u.FoundInSource);
                        if (item.UnitPriceMatched == 0) item.Warnings.Add("② 単価: テンプレートの工種コードが積算予算に1件も見つかりません（すべて 0）。");
                    }
                    catch (Exception e)
                    {
                        item.Warnings.Add("② 単価: " + e.Message);
                    }
                }

                if (string.IsNullOrWhiteSpace(item.ProjectId)) item.Error = "project_id（実行予算表書 C1）が空です。";
                else if (item.ProjectInfo is null && item.UnitPrices is null) item.Error = string.Join(" / ", item.Warnings);

                progress(item.Error is null ? (item.Warnings.Count > 0 ? "warn" : "ok") : "error",
                    $"{item.FileName} → {item.ProjectId ?? "(IDなし)"} {item.ProjectName}{(item.Error is null ? "" : "：" + item.Error)}{(item.Error is null && item.Warnings.Count > 0 ? "：" + string.Join(" / ", item.Warnings) : "")}",
                    i + 1, files.Count);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                item.Error = e is IOException ? $"ファイルを開けません（{e.Message}）" : e.Message;
                progress("error", $"{item.FileName}：{item.Error}", i + 1, files.Count);
            }
        }

        // 同じ project_id のファイルが複数ある場合は警告（登録時はどちらか一方だけ選べる）
        foreach (var g in result.Files.Where(f => f.IsValid).GroupBy(f => f.ProjectId).Where(g => g.Count() > 1))
        {
            foreach (var f in g) f.Warnings.Add($"同じ project_id のファイルが {g.Count()} 件あります。登録はどれか1件だけにしてください。");
            progress("warn", $"project_id {g.Key} が重複しています: {string.Join("、", g.Select(f => f.FileName))}", files.Count, files.Count);
        }

        var ok = result.Files.Count(f => f.IsValid);
        progress("done", $"読込完了: {ok} / {files.Count} 件（エラー {files.Count - ok} 件）", files.Count, files.Count);
        return result;
    }

    private static string? ReadText(object? v)
    {
        var s = v is null ? "" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? "";
        return s.Length == 0 ? null : s;
    }
}
