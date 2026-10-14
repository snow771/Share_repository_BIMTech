using System.Windows;
using SimilaritySearchApp.Bridge;
using SimilaritySearchApp.Settings;

namespace SimilaritySearchApp;

/// <summary>
/// Stencile変換ウィンドウ（別ウィンドウ）。wwwroot/stencil.html を表示し、操作は <see cref="StencilBridge"/> が受ける。
/// DB 設定はメイン画面と共有する。DB に登録したらメイン画面へ知らせる（再読込を促す）。
/// </summary>
public partial class StencilWindow : Window
{
    private readonly StencilBridge _bridge;

    public StencilWindow(AppState state, SettingsStore store, Action onDbChanged)
    {
        InitializeComponent();
        _bridge = new StencilBridge(state, store, () => this,
            postEvent: (name, data) => WebViewHost.PostEvent(WebView, name, data),
            onDbChanged: onDbChanged);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await WebViewHost.InitializeAsync(WebView, "stencil.html", _bridge.HandleAsync);
            StartupMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StartupMessage.Text = "画面の初期化に失敗しました。\n\n" + ex.Message;
        }
    }
}
