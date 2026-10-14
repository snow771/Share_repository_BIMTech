using System.Diagnostics;
using System.IO;
using System.Windows;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Ocr;
using SimilaritySearchApp.Ocr.Azure;
using SimilaritySearchApp.Ocr.Models;
using SimilaritySearchApp.Ocr.Pdf;
using SimilaritySearchApp.Ocr.Structure;
using SimilaritySearchApp.Ocr.Usage;
using SimilaritySearchApp.Settings;

namespace SimilaritySearchApp.Bridge;

/// <summary>
/// 見積AI-OCRウィンドウ（ocr.html）からのコマンドを受ける。
///   getOcrState       … 前回の値・Azure 接続先の状態・同梱ルール・利用枚数
///   pickPath          … フォルダ／ファイル選択（1つ）
///   pickPdfs          … PDF を複数選択してページ数を返す
///   saveAzureSettings … エンドポイント・キー（暗号化して保存）・分割ページ数・利用枚数の上限
///   testAzure         … 接続テスト（ページは消費しない）
///   run               … OCR・構造化（進捗は "ocrProgress"、利用枚数は "ocrUsage" イベントで逐次送る）
///   cancel            … 実行中の処理を止める（実行中でも受け付ける）
///   restructure       … OCR 済み JSON から構造化だけやり直す（Azure を使わない）
///   jobDetail         … 結果の明細・OCR行監査（プレビュー用）
///   openPath          … ファイル（Excel など）やフォルダを開く
///   usage             … 利用枚数
///   estimate…         … 見積DB連携の入口（<see cref="EstimateEntry"/>。DB 取込・確認ウィンドウ）
///   pickEstimateFile  … 見積DB取込用のファイル選択（OCR の実行中でも使える）
/// </summary>
public sealed class OcrBridge
{
    private readonly AppState _state;
    private readonly SettingsStore _store;
    private readonly Func<Window?> _owner;
    private readonly Action<string, object?> _postEvent;
    private readonly Action _onUsageChanged;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly OcrUsageCounter _counter = new();
    private readonly List<OcrJobResult> _results = new();
    private CancellationTokenSource? _running;

    /// <param name="onUsageChanged">利用枚数が増えたとき（メイン画面のホームの表示を更新）</param>
    public OcrBridge(AppState state, SettingsStore store, Func<Window?> owner, Action<string, object?> postEvent, Action onUsageChanged)
    {
        _state = state;
        _store = store;
        _owner = owner;
        _postEvent = postEvent;
        _onUsageChanged = onUsageChanged;
        _estimate = new EstimateEntry(state, owner);
    }

    private readonly EstimateEntry _estimate;

    private OcrSettings Settings => _state.Settings.Ocr;

    public sealed class PathsRequest
    {
        public List<string> Paths { get; set; } = new();
    }

    public sealed class PathRequest
    {
        public string Path { get; set; } = "";
    }

    public sealed class AzureSettingsView
    {
        public string Endpoint { get; set; } = "";

        /// <summary>null は「変更しない」、空文字は「削除」</summary>
        public string? Key { get; set; }
        public int ChunkPages { get; set; } = 2;
        public int Limit { get; set; } = 500;
        public int WarnAt { get; set; } = 300;
        public int DangerAt { get; set; } = 400;
        public string Period { get; set; } = "day";
    }

    public sealed class RestructureRequest
    {
        public string RawJsonPath { get; set; } = "";
        public string CommonRulesPath { get; set; } = "";
        public string? SupplierRulesPath { get; set; }
    }

    public sealed class JobDetailRequest
    {
        public int Index { get; set; }
    }

    public async Task<BridgeResponse> HandleAsync(BridgeRequest req)
    {
        // 中止は実行中（_gate を持っている間）でも受け付ける
        if (req.Command == "cancel")
        {
            var running = _running;
            try
            {
                running?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ちょうど終わったところ
            }
            return new BridgeResponse(req.Id, true, new { canceled = running is not null }, null);
        }

        // 見積DB連携の入口（DB 取込・確認ウィンドウ）は OCR の実行中でも使えるよう、順番待ちの外で処理する
        if (EstimateEntry.Handles(req.Command)) return await _estimate.HandleAsync(req);
        if (req.Command == "pickEstimateFile")
        {
            try
            {
                return new BridgeResponse(req.Id, true, new { path = FileDialogs.Pick(BridgeJson.Read<FileDialogs.PickRequest>(req.Payload), _owner()) }, null);
            }
            catch (Exception e)
            {
                return new BridgeResponse(req.Id, false, null, e.Message);
            }
        }

        await _gate.WaitAsync();
        try
        {
            object? result = req.Command switch
            {
                "getOcrState" => GetState(),
                "pickPath" => new { path = FileDialogs.Pick(BridgeJson.Read<FileDialogs.PickRequest>(req.Payload), _owner()) },
                "pickPdfs" => await PickPdfsAsync(BridgeJson.Read<FileDialogs.PickRequest>(req.Payload)),
                "pdfInfo" => await Task.Run(() => PdfInfos(BridgeJson.Read<PathsRequest>(req.Payload).Paths)),
                "saveAzureSettings" => SaveAzure(BridgeJson.Read<AzureSettingsView>(req.Payload)),
                "testAzure" => await TestAzureAsync(BridgeJson.Read<AzureSettingsView>(req.Payload)),
                "run" => await RunAsync(BridgeJson.Read<OcrRunOptions>(req.Payload)),
                "restructure" => await RestructureAsync(BridgeJson.Read<RestructureRequest>(req.Payload)),
                "jobDetail" => JobDetail(BridgeJson.Read<JobDetailRequest>(req.Payload).Index),
                "openPath" => OpenPath(BridgeJson.Read<PathRequest>(req.Payload).Path),
                "usage" => _counter.Snapshot(Settings),
                _ => throw new InvalidOperationException($"未対応のコマンドです: {req.Command}"),
            };
            return new BridgeResponse(req.Id, true, result, null);
        }
        catch (Exception e)
        {
            return new BridgeResponse(req.Id, false, null, e.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── 状態・設定 ──

    private object GetState()
    {
        var s = Settings;
        var (endpoint, _, source) = AzureLayoutClient.ResolveCredentials(s);
        return new
        {
            settings = new
            {
                s.OutputFolder, s.CommonRulesPath, s.SupplierRulesPath, s.DoStructure, s.KeepRawJson, s.KeepRawExcel, s.KeepChunks,
            },
            azure = AzureView(s, endpoint, source),
            defaultCommonRules = File.Exists(OcrRules.DefaultCommonPath) ? OcrRules.DefaultCommonPath : null,
            rulesFolder = OcrRules.AssetsFolder,
            usage = _counter.Snapshot(s),
            results = _results,
        };
    }

    private static object AzureView(OcrSettings s, string endpoint, string source) => new
    {
        endpoint = s.Endpoint,
        effectiveEndpoint = endpoint,
        hasKey = !string.IsNullOrEmpty(s.EncryptedKey),
        source,   // 設定 / 環境変数 / 未設定
        s.ChunkPages, s.Limit, s.WarnAt, s.DangerAt, s.Period,
    };

    private object SaveAzure(AzureSettingsView v)
    {
        var s = Settings;
        Apply(v, s);
        _store.Save(_state.Settings);
        _onUsageChanged();
        var (endpoint, _, source) = AzureLayoutClient.ResolveCredentials(s);
        return new { azure = AzureView(s, endpoint, source), usage = _counter.Snapshot(s) };
    }

    /// <summary>画面の入力を設定に重ねる（キーは null なら現状維持）。値はあり得る範囲に収める。</summary>
    private static void Apply(AzureSettingsView v, OcrSettings s)
    {
        s.Endpoint = v.Endpoint.Trim();
        if (v.Key is not null) s.EncryptedKey = SettingsStore.Protect(v.Key.Trim());
        s.ChunkPages = Math.Clamp(v.ChunkPages, 1, 50);
        s.Limit = Math.Max(1, v.Limit);
        s.DangerAt = Math.Clamp(v.DangerAt, 0, s.Limit);
        s.WarnAt = Math.Clamp(v.WarnAt, 0, s.DangerAt);
        s.Period = v.Period == "month" ? "month" : "day";
    }

    private async Task<object> TestAzureAsync(AzureSettingsView v)
    {
        // 保存前の入力でテストする（設定はまだ変えない）
        var trial = new OcrSettings { EncryptedKey = Settings.EncryptedKey };
        Apply(v, trial);
        using var client = AzureLayoutClient.FromSettings(trial);
        var (_, _, source) = AzureLayoutClient.ResolveCredentials(trial);
        var message = await client.TestAsync();
        return new { message = source == "環境変数" ? message + "（環境変数 DI_ENDPOINT / DI_KEY を使用）" : message };
    }

    // ── PDF ──

    private async Task<object> PickPdfsAsync(FileDialogs.PickRequest req)
    {
        var paths = FileDialogs.PickMany(req, _owner());
        return new { files = await Task.Run(() => PdfInfos(paths)) };
    }

    private static List<PdfFileInfo> PdfInfos(IEnumerable<string> paths) =>
        paths.Select(p =>
        {
            try
            {
                return new PdfFileInfo(p, Path.GetFileName(p), PdfSplitter.CountPages(p), null);
            }
            catch (Exception e)
            {
                return new PdfFileInfo(p, Path.GetFileName(p), null, e.Message);
            }
        }).ToList();

    // ── 実行 ──

    private async Task<object> RunAsync(OcrRunOptions o)
    {
        // 次回の初期値として保存
        var s = Settings;
        s.OutputFolder = o.OutputFolder;
        s.CommonRulesPath = o.CommonRulesPath;
        s.SupplierRulesPath = o.SupplierRulesPath ?? "";
        s.DoStructure = o.DoStructure;
        s.KeepRawJson = o.KeepRawJson;
        s.KeepRawExcel = o.KeepRawExcel;
        s.KeepChunks = o.KeepChunks;
        _store.Save(_state.Settings);

        using var cts = new CancellationTokenSource();
        _running = cts;
        try
        {
            var runner = new OcrJobRunner(s, _counter,
                (level, message, done, total) => _postEvent("ocrProgress", new { level, message, done, total }),
                () =>
                {
                    _postEvent("ocrUsage", _counter.Snapshot(s));
                    _onUsageChanged();
                });
            var jobs = await Task.Run(() => runner.RunAsync(o, cts.Token));
            var start = _results.Count;
            foreach (var j in jobs) j.Index = start + j.Index;
            _results.AddRange(jobs);
            return new { jobs, usage = _counter.Snapshot(s) };
        }
        finally
        {
            _running = null;
        }
    }

    private async Task<object> RestructureAsync(RestructureRequest r)
    {
        if (!File.Exists(r.CommonRulesPath)) throw new InvalidOperationException("共通ルール JSON を選択してください。");
        var job = await Task.Run(() => OcrJobRunner.Restructure(r.RawJsonPath, r.CommonRulesPath,
            string.IsNullOrWhiteSpace(r.SupplierRulesPath) ? null : r.SupplierRulesPath));
        job.Index = _results.Count;
        _results.Add(job);
        return new { job };
    }

    private object JobDetail(int index)
    {
        var job = _results.FirstOrDefault(j => j.Index == index) ?? throw new InvalidOperationException("結果が見つかりません。");
        return new { job, items = job.Structured?.Items ?? new(), audit = job.Structured?.AuditRows ?? new() };
    }

    private static object OpenPath(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return new { opened = true };
        }
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return new { opened = true };
        }
        throw new FileNotFoundException($"見つかりません: {path}");
    }
}
