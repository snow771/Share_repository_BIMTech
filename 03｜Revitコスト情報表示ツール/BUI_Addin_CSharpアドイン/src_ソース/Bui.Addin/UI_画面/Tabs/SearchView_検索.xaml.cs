// このファイルは、検索タブのXAMLを必ず初期化するWPFコードビハインドです。
// XAML生成コードに依存し、コントロール追加時も検索ロジックはViewModel側に保ちます。
using System.Windows.Controls;
namespace Bui.UI.Tabs;
public partial class SearchView : UserControl
{
    public SearchView() => InitializeComponent();
}
