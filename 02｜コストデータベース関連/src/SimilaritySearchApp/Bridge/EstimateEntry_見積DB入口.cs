using System.Windows;
using SimilaritySearchApp.Estimate.Import;
using SimilaritySearchApp.Estimate.Models;

namespace SimilaritySearchApp.Bridge;

/// <summary>
/// AI-OCR 画面に置いた「見積DB連携」の入口（OcrBridge から呼ぶ）。
///   estimateImportPreview … 取込前の確認（登録済みかどうか・件数・資料）
///   estimateImport        … DB へ取り込んで、確認・取決めウィンドウを開く
///   openEstimateWindow    … 保存済みの見積の確認・取決めウィンドウを開く
///   openEstimateList      … 保存済み見積の一覧ウィンドウを開く
/// OCR の実行中でも使えるよう、OcrBridge の順番待ち（SemaphoreSlim）の外で処理する。
/// </summary>
public sealed class EstimateEntry
{
    private static readonly HashSet<string> Commands = new() { "estimateImportPreview", "estimateImport", "openEstimateWindow", "openEstimateList" };

    private readonly AppState _state;
    private readonly Func<Window?> _owner;

    public EstimateEntry(AppState state, Func<Window?> owner)
    {
        _state = state;
        _owner = owner;
    }

    public static bool Handles(string command) => Commands.Contains(command);

    public sealed class HeaderRequest
    {
        public int HeaderId { get; set; }
    }

    public async Task<BridgeResponse> HandleAsync(BridgeRequest req)
    {
        try
        {
            var service = new EstimateImportService(_state.Settings.Db);
            object? result = req.Command switch
            {
                "estimateImportPreview" => await service.PreviewAsync(BridgeJson.Read<ImportRequest>(req.Payload)),
                "estimateImport" => await ImportAndOpenAsync(service, BridgeJson.Read<ImportRequest>(req.Payload)),
                "openEstimateWindow" => Open(BridgeJson.Read<HeaderRequest>(req.Payload).HeaderId),
                "openEstimateList" => Open(null),
                _ => throw new InvalidOperationException($"未対応のコマンドです: {req.Command}"),
            };
            return new BridgeResponse(req.Id, true, result, null);
        }
        catch (Exception e)
        {
            return new BridgeResponse(req.Id, false, null, e.Message);
        }
    }

    private async Task<ImportResult> ImportAndOpenAsync(EstimateImportService service, ImportRequest req)
    {
        var result = await service.ImportAsync(req);
        Open(result.HeaderId);
        return result;
    }

    private object Open(int? headerId)
    {
        Application.Current.Dispatcher.Invoke(() => EstimateReviewWindow.Show(_state, headerId, _owner()));
        return new { opened = true };
    }
}
