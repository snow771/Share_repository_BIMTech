// このファイルはBUIのWPF画面をRevitドッキングパネルへ渡し、初期表示位置を右側に指定します。
// RevitのIDockablePaneProviderとBuiDockPaneに依存し、画面型や初期状態を変えるとパネル登録と配置に影響します。
using Autodesk.Revit.UI;
using Bui.Data;
using Bui.Revit.ExternalEvents;
using Bui.UI;

namespace Bui.Revit;

/// <summary>RevitへBUIパネルの画面と初期配置を提供します。</summary>
public sealed class BuiPaneProvider : IDockablePaneProvider
{
    private readonly BuiDockPane _pane;

    public BuiPaneProvider(
        RevitActionDispatcher actionDispatcher)
    {
        ArgumentNullException.ThrowIfNull(actionDispatcher);
        // 構成クラスでメタデータ、CSV、Revit境界を一度だけ接続します。
        var composition = BuiDockPaneComposition.Create(actionDispatcher, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        _pane = new BuiDockPane(composition.ViewModel, actionDispatcher);
        ProductRepository = composition.ProductRepository;
    }

    public BuiDockPaneViewModel ViewModel => _pane.ViewModel;

    /// <summary>
    /// パネルが使うのと同じ製品リポジトリです。仕上表出力（Task 8）などパネル外の
    /// コマンドがBuiSession経由で同じデータソースを参照するために公開します。
    /// </summary>
    public IProductRepository ProductRepository { get; }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        // Revitが作るドッキング枠の中身と、初回に表示する位置をここで指定します。
        data.FrameworkElement = _pane;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right
        };
    }
}
