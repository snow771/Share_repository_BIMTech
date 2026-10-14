using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SimilaritySearchApp;

/// <summary>
/// アプリの起動点。メインウィンドウを開き、想定外の例外を表示する。
/// 例外はすべて %AppData%\SimilaritySearchApp\error.log にも記録する（画面の処理・別スレッドの処理とも）。
/// </summary>
public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // 別スレッドで起きた例外（これがあるとアプリが落ちる）も記録する
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is not Exception ex) return;
            ErrorLog.Write("別スレッドの例外（アプリ終了）", ex);
            MessageBox.Show($"{ex.Message}\n\n詳細: {ErrorLog.PathName}", "予期しないエラー（アプリを終了します）", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write("待たれていないタスクの例外", args.Exception);
            args.SetObserved();
        };

        // 見積確認で取り出した資料の一時ファイルのうち、前回消せなかったものを消す
        Estimate.DocumentViewer.CleanupOld();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("画面の処理の例外", e.Exception);
        MessageBox.Show($"{e.Exception.Message}\n\n詳細: {ErrorLog.PathName}", "予期しないエラー", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
