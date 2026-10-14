using System.ComponentModel;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using SimilaritySearchApp.Bridge;
using SimilaritySearchApp.Estimate;

namespace SimilaritySearchApp;

/// <summary>
/// 見積確認・取決めウィンドウ（AI-OCR ウィンドウを Owner にした別ウィンドウ）。wwwroot/estimate-review.html を表示する。
///   見積ID あり … その見積の確認・紐づけ・取決め（同じ見積のウィンドウは 1 つだけ。開いていれば前面へ）
///   見積ID なし … 保存済み見積の一覧（1 つだけ）
/// 資料（PDF・Excel）はウィンドウごとの一時フォルダに取り出し、閉じたら消す。
/// 保存していない変更があるときだけ、閉じる前に確認する。
/// </summary>
public partial class EstimateReviewWindow : Window
{
    private static readonly Dictionary<int, EstimateReviewWindow> Opened = new();
    private const int ListKey = 0;

    private readonly int? _headerId;
    private readonly DocumentViewer _viewer = new();
    private readonly EstimateReviewBridge _bridge;
    private bool _closeConfirmed;

    private EstimateReviewWindow(AppState state, int? headerId)
    {
        InitializeComponent();
        _headerId = headerId;
        Title = headerId is int id ? $"見積確認・取決め ─ 見積 #{id}" : "保存済み見積（見積DB）";
        _bridge = new EstimateReviewBridge(state, headerId, _viewer, () => this);
    }

    /// <summary>ウィンドウを開く（既に開いていれば前面へ）。</summary>
    public static void Show(AppState state, int? headerId, Window? owner)
    {
        var key = headerId ?? ListKey;
        if (Opened.TryGetValue(key, out var existing) && existing.IsLoaded)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var w = new EstimateReviewWindow(state, headerId);
        if (owner is { IsLoaded: true }) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Opened[key] = w;
        w.Show();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await WebViewHost.InitializeAsync(WebView, _headerId is int id ? $"estimate-review.html?id={id}" : "estimate-review.html", _bridge.HandleAsync);
            // DB から取り出した PDF を画面内で表示するための割り当て（このウィンドウの一時フォルダだけ）
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(DocumentViewer.VirtualHost, _viewer.Folder, CoreWebView2HostResourceAccessKind.Allow);
            StartupMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StartupMessage.Text = "画面の初期化に失敗しました。\n\n" + ex.Message;
        }
    }

    /// <summary>保存していない変更があるときだけ確認する（画面側の window.__estimateDirty）。</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || WebView.CoreWebView2 is null) return;
        e.Cancel = true;
        var dirty = "false";
        try
        {
            dirty = await WebView.CoreWebView2.ExecuteScriptAsync("window.__estimateDirty === true");
        }
        catch
        {
            // 画面が応答しなければそのまま閉じる
        }
        if (dirty == "true" &&
            MessageBox.Show(this, "保存していない変更があります。破棄して閉じますか？", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        _closeConfirmed = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        var key = _headerId ?? ListKey;
        if (Opened.TryGetValue(key, out var w) && ReferenceEquals(w, this)) Opened.Remove(key);
        _viewer.Dispose();
    }
}
