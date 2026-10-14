using System.Windows;
using SimilaritySearchApp.Bridge;
using SimilaritySearchApp.Settings;

namespace SimilaritySearchApp;

/// <summary>
/// 見積AI-OCRウィンドウ（別ウィンドウ）。wwwroot/ocr.html を表示し、操作は <see cref="OcrBridge"/> が受ける。
/// 利用枚数が増えたらメイン画面へ知らせる（ホームの表示を更新）。
/// </summary>
public partial class OcrWindow : Window
{
    private readonly OcrBridge _bridge;

    public OcrWindow(AppState state, SettingsStore store, Action onUsageChanged)
    {
        InitializeComponent();
        _bridge = new OcrBridge(state, store, () => this,
            postEvent: (name, data) => WebViewHost.PostEvent(WebView, name, data),
            onUsageChanged: onUsageChanged);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await WebViewHost.InitializeAsync(WebView, "ocr.html", _bridge.HandleAsync);
            StartupMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StartupMessage.Text = "画面の初期化に失敗しました。\n\n" + ex.Message;
        }
    }
}
