using System.Windows;
using SimilaritySearchApp.Bridge;
using SimilaritySearchApp.Settings;

namespace SimilaritySearchApp;

/// <summary>
/// メインウィンドウ。WebView2 で wwwroot の画面を表示し、画面とのメッセージを <see cref="WebBridge"/> に渡すだけ。
/// ヘッダーの「🧰 Stencile変換」から <see cref="StencilWindow"/>、「🧾 AI-OCR」から <see cref="OcrWindow"/> を開く
/// （それぞれ1つだけ。開いていれば前面へ）。
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppState _state;
    private readonly SettingsStore _store;
    private readonly WebBridge _bridge;
    private StencilWindow? _stencilWindow;
    private OcrWindow? _ocrWindow;

    public MainWindow()
    {
        InitializeComponent();
        _store = new SettingsStore();
        _state = new AppState { Settings = _store.Load() };
        _bridge = new WebBridge(_state, _store, OpenStencilWindow, OpenOcrWindow, () => this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await WebViewHost.InitializeAsync(WebView, "index.html", _bridge.HandleAsync);
            StartupMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StartupMessage.Text =
                "画面の初期化に失敗しました。WebView2 ランタイムがインストールされているか確認してください。\n\n" + ex.Message;
        }
    }

    private void OpenStencilWindow()
    {
        if (_stencilWindow is { IsLoaded: true })
        {
            if (_stencilWindow.WindowState == WindowState.Minimized) _stencilWindow.WindowState = WindowState.Normal;
            _stencilWindow.Activate();
            return;
        }
        _stencilWindow = new StencilWindow(_state, _store, onDbChanged: () => WebViewHost.PostEvent(WebView, "dbChanged", new { source = "stencil" }))
        {
            Owner = null,   // メイン画面と並べて使えるよう、親子関係は付けない
        };
        _stencilWindow.Closed += (_, _) => _stencilWindow = null;
        _stencilWindow.Show();
    }

    private void OpenOcrWindow()
    {
        if (_ocrWindow is { IsLoaded: true })
        {
            if (_ocrWindow.WindowState == WindowState.Minimized) _ocrWindow.WindowState = WindowState.Normal;
            _ocrWindow.Activate();
            return;
        }
        _ocrWindow = new OcrWindow(_state, _store, onUsageChanged: () => WebViewHost.PostEvent(WebView, "ocrUsageChanged", null))
        {
            Owner = null,
        };
        _ocrWindow.Closed += (_, _) => _ocrWindow = null;
        _ocrWindow.Show();
    }
}
