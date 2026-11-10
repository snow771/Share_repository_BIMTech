// このファイルの目的:
//   AIチャットの別ウィンドウです。状態は BuiChatViewModel が持ちます。
//
// なぜ閉じずに隠すのか:
//   会話を残したまま開き直せるようにするためです。
//   開き直したときは選択中の情報を必ず読み直します（RefreshContext）。
//   古い部材について答え続けるのを防ぐためです。

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Bui.UI;

public partial class BuiChatWindow : Window
{
    private readonly BuiChatViewModel _viewModel;

    // ForceClose経由でだけ閉じることを許すための抜け道です。既定はfalseで、
    // OnClosingは常にキャンセルして隠します（会話を残すため）。アドイン終了時など、
    // ウィンドウを本当に破棄しなければならないときだけForceCloseからtrueにします。
    private bool _allowClose;

    public BuiChatWindow(BuiChatViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    /// <summary>開き直すたびに選択中の情報を読み直します。</summary>
    public void ShowRefreshed()
    {
        _viewModel.RefreshContext();
        Show();
        Activate();
    }

    /// <summary>
    /// アドイン終了時などに、隠すのではなく本当にウィンドウを破棄するための抜け道です。
    /// これが無いと、OnClosingが常にキャンセルするため誰もこのウィンドウを閉じられず、
    /// アドインが解放された後もパネルViewModel（既にDisposeされ得る）へ束縛されたまま残ります。
    /// </summary>
    internal void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose) return;

        // 閉じずに隠します。会話を残したまま次に開けるようにするためです。
        e.Cancel = true;
        Hide();
    }

    private async void OnSendPlanToSearchTab(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChatMessageViewModel message }) return;

        try
        {
            await _viewModel.SendPlanToSearchTabAsync(message).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 利用者の取消は障害ではありません。
        }
        catch (Exception exception)
        {
            // このハンドラはasync voidのイベントハンドラです。他のすべての操作はAsyncCommand経由で
            // IAsyncCommandErrorHandlerへ例外を渡しますが、ここはボタンのClickイベントから直接
            // 呼ばれるため、拾わないとRevitのUIスレッドへ未処理例外が抜けます。
            _viewModel.ReportUnhandledException(exception);
        }
    }
}
