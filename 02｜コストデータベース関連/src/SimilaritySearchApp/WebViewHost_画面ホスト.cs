using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SimilaritySearchApp.Bridge;

namespace SimilaritySearchApp;

/// <summary>
/// WebView2 の共通初期化（メイン画面・Stencile変換ウィンドウで共用）。
///   ・ユーザーデータは LocalAppData（環境は1つを共有）
///   ・wwwroot を https://app.local/ に割り当て
///   ・画面からの { id, command, payload } を受けてハンドラに渡し、応答を返す
///   ・C# から画面へのお知らせ（進捗など）は PostEvent で { event, data } を送る
/// </summary>
public static class WebViewHost
{
    public const string VirtualHost = "app.local";

    private static Task<CoreWebView2Environment>? _environment;
    private static bool _cacheCleared;

    private static Task<CoreWebView2Environment> EnvironmentAsync()
    {
        _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimilaritySearchApp", "WebView2"));
        return _environment;
    }

    /// <summary>WebView2 を初期化して page（例: index.html）を開く。</summary>
    public static async Task InitializeAsync(WebView2 webView, string page, Func<BridgeRequest, Task<BridgeResponse>> handler)
    {
        await webView.EnsureCoreWebView2Async(await EnvironmentAsync());
        var core = webView.CoreWebView2;

        // 画面ファイル（CSS/JS）を更新したときに古いキャッシュが使われないよう、起動して最初の1回だけ消す
        if (!_cacheCleared)
        {
            _cacheCleared = true;
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
        }

        core.SetVirtualHostNameToFolderMapping(VirtualHost, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDevToolsEnabled = true;   // F12 で開発者ツール
        core.Settings.IsStatusBarEnabled = false;
        core.WebMessageReceived += async (_, e) =>
        {
            BridgeRequest? request = null;
            try
            {
                request = JsonSerializer.Deserialize<BridgeRequest>(e.WebMessageAsJson, BridgeJson.Options);
            }
            catch (JsonException)
            {
            }
            if (request is null || string.IsNullOrEmpty(request.Id)) return;

            // WebView2 のイベントの中でファイル選択などのダイアログを開くと、WebView2 が落ちることがある（再入禁止）。
            // いったんイベントを抜けてから処理する。
            await Task.Yield();
            string json;
            try
            {
                var response = await handler(request);
                json = JsonSerializer.Serialize(response, BridgeJson.Options);
            }
            catch (Exception ex)
            {
                // ここで例外を外に出すとアプリごと終了するため、画面にエラーとして返す
                ErrorLog.Write($"コマンド {request.Command}", ex);
                json = JsonSerializer.Serialize(new BridgeResponse(request.Id, false, null, ex.Message), BridgeJson.Options);
            }
            try
            {
                webView.CoreWebView2?.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("画面への応答", ex);   // ウィンドウを閉じた直後など
            }
        };
        core.Navigate($"https://{VirtualHost}/{page}");
    }

    /// <summary>画面へお知らせを送る（どのスレッドから呼んでもよい）。</summary>
    public static void PostEvent(WebView2 webView, string name, object? data)
    {
        var json = JsonSerializer.Serialize(new { @event = name, data }, BridgeJson.Options);
        webView.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                webView.CoreWebView2?.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                ErrorLog.Write($"画面へのお知らせ {name}", ex);   // ウィンドウを閉じた直後など
            }
        });
    }
}
