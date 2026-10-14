using System.Diagnostics;
using System.IO;
using System.Windows;
using SimilaritySearchApp.Settings;
using SimilaritySearchApp.Stencil;

namespace SimilaritySearchApp.Bridge;

/// <summary>
/// Stencile変換ウィンドウ（stencil.html）からのコマンドを受ける。
///   getStencilState … 前回の設定・同梱マッピング・接続先
///   pickPath        … フォルダ／ファイル選択
///   scan            … 読込（進捗は "stencilProgress" イベントで逐次送る）→ DB の既存有無を照合
///   exportExcel     … 読込結果を Excel に保存
///   register        … 選んだ物件を DB に登録（1トランザクション）
///   openFolder      … 出力先をエクスプローラーで開く
/// 読込結果（単価行を含む）は C# 側に保持し、画面には一覧に必要な分だけ送る。
/// </summary>
public sealed class StencilBridge
{
    private readonly AppState _state;
    private readonly SettingsStore _store;
    private readonly Func<Window?> _owner;
    private readonly Action<string, object?> _postEvent;
    private readonly Action _onDbChanged;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StencilScanResult? _lastScan;

    public StencilBridge(AppState state, SettingsStore store, Func<Window?> owner, Action<string, object?> postEvent, Action onDbChanged)
    {
        _state = state;
        _store = store;
        _owner = owner;
        _postEvent = postEvent;
        _onDbChanged = onDbChanged;
    }

    public sealed class ScanRequestView
    {
        public string SourceFolder { get; set; } = "";
        public bool IncludeSubfolders { get; set; } = true;
        public bool DoProjectInfo { get; set; } = true;
        public bool DoUnitPrice { get; set; } = true;
        public string? MappingPath { get; set; }
        public string? TemplatePath { get; set; }
    }

    public sealed class ExportRequest
    {
        public string OutputFolder { get; set; } = "";
    }

    public sealed class RegisterRequest
    {
        public List<int> Indexes { get; set; } = new();
        public bool DoProjectInfo { get; set; } = true;
        public bool DoUnitPrice { get; set; } = true;
    }

    public sealed class PathRequest
    {
        public string Path { get; set; } = "";
    }

    public async Task<BridgeResponse> HandleAsync(BridgeRequest req)
    {
        await _gate.WaitAsync();
        try
        {
            object? result = req.Command switch
            {
                "getStencilState" => GetState(),
                "pickPath" => new { path = FileDialogs.Pick(BridgeJson.Read<FileDialogs.PickRequest>(req.Payload), _owner()) },
                "scan" => await ScanAsync(BridgeJson.Read<ScanRequestView>(req.Payload)),
                "exportExcel" => await ExportAsync(BridgeJson.Read<ExportRequest>(req.Payload)),
                "register" => await RegisterAsync(BridgeJson.Read<RegisterRequest>(req.Payload)),
                "openFolder" => OpenFolder(BridgeJson.Read<PathRequest>(req.Payload).Path),
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

    private object GetState()
    {
        var s = _state.Settings;
        return new
        {
            settings = s.Stencil,
            defaultMapping = File.Exists(StencilMapping.DefaultPath) ? StencilMapping.DefaultPath : null,
            database = $"{s.Db.Server} / {s.Db.Database}",
            tables = new { projects = s.Db.Tables.Projects, assess = s.Db.Tables.Assess, revise = s.Db.Tables.Revise },
            lastScan = _lastScan,
        };
    }

    private async Task<StencilScanResult> ScanAsync(ScanRequestView v)
    {
        // 次回の初期値として保存
        var st = _state.Settings.Stencil;
        st.SourceFolder = v.SourceFolder;
        st.IncludeSubfolders = v.IncludeSubfolders;
        st.DoProjectInfo = v.DoProjectInfo;
        st.DoUnitPrice = v.DoUnitPrice;
        st.MappingPath = v.MappingPath ?? "";
        st.TemplatePath = v.TemplatePath ?? "";
        _store.Save(_state.Settings);

        var req = new StencilScanRequest
        {
            SourceFolder = v.SourceFolder, IncludeSubfolders = v.IncludeSubfolders,
            DoProjectInfo = v.DoProjectInfo, DoUnitPrice = v.DoUnitPrice,
            MappingPath = string.IsNullOrWhiteSpace(v.MappingPath) ? null : v.MappingPath,
            TemplatePath = v.TemplatePath,
        };
        var scan = await Task.Run(() => StencilScanService.Scan(req,
            (level, message, done, total) => _postEvent("stencilProgress", new { level, message, done, total })));

        // DB の既存有無（接続できなくても Excel 出力はできるので、失敗は警告にとどめる）
        try
        {
            _postEvent("stencilProgress", new { level = "step", message = "DB の登録状況を確認しています...", done = 0, total = 0 });
            await new StencilDbRegistrar(_state.Settings.Db).CheckExistingAsync(scan);
            var existing = scan.Files.Count(f => f.IsValid && f.ExistsInDb);
            _postEvent("stencilProgress", new { level = "ok", message = $"DB 照合: 既存 {existing} 件 / 新規 {scan.Files.Count(f => f.IsValid) - existing} 件", done = 0, total = 0 });
        }
        catch (Exception e)
        {
            scan.DbChecked = false;
            scan.DbMessage = "DB に接続できないため、登録状況を確認できませんでした（Excel 出力は可能）: " + e.Message;
            _postEvent("stencilProgress", new { level = "warn", message = scan.DbMessage, done = 0, total = 0 });
        }

        _lastScan = scan;
        return scan;
    }

    private async Task<object> ExportAsync(ExportRequest r)
    {
        var scan = _lastScan ?? throw new InvalidOperationException("先に「読み込む」を実行してください。");
        if (string.IsNullOrWhiteSpace(r.OutputFolder)) throw new InvalidOperationException("保存先フォルダを選んでください。");
        _state.Settings.Stencil.OutputFolder = r.OutputFolder;
        _store.Save(_state.Settings);
        var files = await Task.Run(() => StencilExcelWriter.Write(scan, scan.Files, r.OutputFolder));
        return new { files };
    }

    private async Task<object> RegisterAsync(RegisterRequest r)
    {
        var scan = _lastScan ?? throw new InvalidOperationException("先に「読み込む」を実行してください。");
        var registrar = new StencilDbRegistrar(_state.Settings.Db);
        var result = await registrar.RegisterAsync(scan, r.Indexes, r.DoProjectInfo && scan.DoProjectInfo, r.DoUnitPrice && scan.DoUnitPrice);

        // 登録後の状態で一覧を更新し、メイン画面にも知らせる
        try
        {
            await registrar.CheckExistingAsync(scan);
        }
        catch
        {
            // 照合のやり直しに失敗しても登録は完了している
        }
        _onDbChanged();
        return new { result, scan };
    }

    private static object OpenFolder(string path)
    {
        var target = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException($"フォルダが見つかりません: {target}");
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
        return new { opened = true };
    }
}
