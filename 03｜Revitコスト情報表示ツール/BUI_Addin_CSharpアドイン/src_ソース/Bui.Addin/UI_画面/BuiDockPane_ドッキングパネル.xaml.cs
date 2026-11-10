// このファイルは、6タブXAMLに共通ViewModelを設定する最小のWPFホストです。
// BuiDockPaneViewModelとRevitActionDispatcherに依存し、変更はパネル全タブのバインド先に影響します。
using System.Windows.Controls;
using Bui.Revit.ExternalEvents;

namespace Bui.UI;

/// <summary>Revitのドッキング枠に入るBUIページです。</summary>
public partial class BuiDockPane : Page
{
    public BuiDockPane(BuiDockPaneViewModel viewModel, RevitActionDispatcher actionDispatcher)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ActionDispatcher = actionDispatcher ?? throw new ArgumentNullException(nameof(actionDispatcher));
        InitializeComponent();
        DataContext = ViewModel;
    }

    public BuiDockPaneViewModel ViewModel { get; }

    /// <summary>Product_ID等の書込操作が共有する唯一のExternalEvent窓口です。</summary>
    public RevitActionDispatcher ActionDispatcher { get; }
}
