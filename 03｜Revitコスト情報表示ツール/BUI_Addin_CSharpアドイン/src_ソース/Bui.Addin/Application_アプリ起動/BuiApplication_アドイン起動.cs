// このファイルはRevit起動時にBUIのリボンとドッキングパネルを登録し、終了時に所有リソースを解放します。
// Revit UI API、RibbonService、BuiPaneProviderに依存し、起動順序や後始末を変えるとアドイン全体の読込みとRevit終了処理に影響します。
using Autodesk.Revit.UI;
using Bui.Resources;
using Bui.Revit;
using Bui.Revit.ExternalEvents;
using Bui.Revit.Selection;
using Bui.UI;

namespace Bui.Application;

/// <summary>RevitセッションにBUIの入口を追加する外部アプリケーションです。</summary>
public sealed class BuiApplication : IExternalApplication
{
    private readonly List<Action> _ownedEventUnsubscribers = new();
    private readonly List<IDisposable> _ownedResources = new();

    public Result OnStartup(UIControlledApplication application)
    {
        var startup = BuiRuntimeSafety.Run<object>("BUI_STARTUP", () =>
        {
            RibbonService.Create(application);

            // モデルレス画面からの全Revit操作は、この一つのExternalEventを共有します。
            var actionDispatcher = new RevitActionDispatcher();
            _ownedResources.Add(actionDispatcher);

            var paneProvider = new BuiPaneProvider(actionDispatcher);
            _ownedResources.Add(paneProvider.ViewModel);
            var paneId = new DockablePaneId(Identifiers.DockablePaneId);
            application.RegisterDockablePane(paneId, "BUI", paneProvider);

            // リボンの「AIに聞く」からパネルの状態を、仕上表出力（Task 8）から製品リポジトリを
            // 参照するため、ここで共有します。
            BuiSession.Set(paneProvider.ViewModel, paneProvider.ProductRepository);

            // ViewModel自身が選択通知を受け、古い非同期結果の上書きを防ぎます。
            var selectionTracker = new SelectionTracker(paneProvider.ViewModel);
            selectionTracker.Subscribe(application);
            // ViewActivatedも同じメソッド参照で登録・解除し、文書/ビュー切替時に旧ElementIdを失効させます。
            application.ViewActivated += selectionTracker.OnViewActivated;
            _ownedEventUnsubscribers.Add(() =>
            {
                application.ViewActivated -= selectionTracker.OnViewActivated;
                selectionTracker.Unsubscribe(application);
            });

            return true;
        });
        if (!startup.IsSuccess)
        {
            // 例外の生文は表示せず、秘密値や社内パスがRevit画面へ出ることを防ぎます。
            _ = BuiRuntimeSafety.Run<object>("BUI_STARTUP_CLEANUP", () =>
            {
                CleanupOwnedResources();
                return true;
            });
            TaskDialog.Show("BUI", $"BUIの初期化に失敗しました。\n{startup.Error!.UserMessage}");
            return Result.Failed;
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        // 終了時の解放失敗もRevit本体の終了を妨げないよう、同じ安全境界で囲みます。
        _ = BuiRuntimeSafety.Run<object>("BUI_SHUTDOWN", () =>
        {
            CleanupOwnedResources();
            return true;
        });
        return Result.Succeeded;
    }

    private void CleanupOwnedResources()
    {
        BuiSession.Clear();
        // AIチャットウィンドウはOnClosingで常に隠すだけなので、ここで明示的に閉じないと
        // アドイン終了後もDisposeされたパネルViewModelへ束縛されたまま残ります。
        AskBuiAiCommand.CloseWindow();

        // 後続機能が登録するRevitイベントは、登録と逆順で解除して終了中のコールバックを防ぎます。
        for (var index = _ownedEventUnsubscribers.Count - 1; index >= 0; index--)
        {
            _ownedEventUnsubscribers[index]();
        }

        _ownedEventUnsubscribers.Clear();

        // ExternalEventを含む所有リソースはRevit終了前に必ずDisposeします。
        for (var index = _ownedResources.Count - 1; index >= 0; index--)
        {
            _ownedResources[index].Dispose();
        }

        _ownedResources.Clear();
    }
}
