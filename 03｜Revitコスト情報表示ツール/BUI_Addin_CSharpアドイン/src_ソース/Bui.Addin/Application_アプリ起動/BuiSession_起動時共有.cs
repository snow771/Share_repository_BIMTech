// このファイルの目的:
//   起動時に作った画面の状態と製品リポジトリを、リボンのコマンドから参照できるようにします。
//
// なぜ静的な入れ物なのか:
//   RevitのIExternalCommandは毎回新しく作られ、コンストラクタ引数を渡せません。
//   BuiApplicationがOnStartupで入れ、OnShutdownで必ず空にします。
//
// なぜProductRepositoryも持つのか:
//   仕上表出力コマンド（Task 8）はBUIパネルの外から起動する独立したコマンドで、
//   BuiDockPaneComposition.Createが作ったのと同じ製品データソースを、
//   パネルとは別経路で参照する必要があります。二重にリポジトリを作ると、
//   パネル側で切り替えたデータソースと仕上表出力側が食い違う恐れがあるため、
//   パネル構成が作った同一インスタンスをここで共有します。
//
// 修正時の影響:
//   OnShutdownでの解放を外すと、Revit終了後もViewModelとProductRepositoryが残ります。

using Bui.Data;
using Bui.UI;

namespace Bui.Application;

/// <summary>起動時に作った共有物です。BuiApplicationだけが書き換えます。</summary>
internal static class BuiSession
{
    public static BuiDockPaneViewModel? PaneViewModel { get; private set; }
    public static IProductRepository? ProductRepository { get; private set; }

    public static void Set(BuiDockPaneViewModel viewModel, IProductRepository productRepository)
    {
        PaneViewModel = viewModel;
        ProductRepository = productRepository;
    }

    public static void Clear()
    {
        PaneViewModel = null;
        ProductRepository = null;
    }
}
