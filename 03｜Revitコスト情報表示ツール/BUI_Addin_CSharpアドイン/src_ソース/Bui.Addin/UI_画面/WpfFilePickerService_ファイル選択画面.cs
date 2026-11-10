// このファイルの目的:
//   設定タブの「参照」ボタンから、WindowsのファイルダイアログでCSVを選べるようにします。
//
// なぜ画面側にあるか:
//   ファイルダイアログはWPF（Microsoft.Win32）に依存します。
//   ViewModelをWPF非依存に保つため、実体だけをこちら側に置き、
//   ViewModelは IFilePickerService という契約だけを見ます。
//   テストでは偽の実装に差し替えられます。
//
// 修正時の影響:
//   ダイアログはUIスレッドで開く必要があります。_ui.InvokeAsync を外すと
//   Revit上でスレッド違反の例外になります。

using System.Windows;

namespace Bui.UI;

/// <summary>WPFのファイルダイアログでCSVの場所を選ばせる実装です。</summary>
public sealed class WpfFilePickerService : IFilePickerService
{
    private readonly IUiDispatcher _ui;

    public WpfFilePickerService(IUiDispatcher ui) =>
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));

    /// <summary>既存のCSVを選びます。製品CSV・単価CSV用です。</summary>
    public Task<string?> PickExistingFileAsync(
        string title, string filter, string? initialPath, CancellationToken cancellationToken) =>
        ShowAsync(new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        }, initialPath, cancellationToken);

    /// <summary>まだ存在しないファイル名も指定できます。これから作る構成CSV用です。</summary>
    public Task<string?> PickNewOrExistingFileAsync(
        string title, string filter, string? initialPath, CancellationToken cancellationToken) =>
        ShowAsync(new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = filter,
            // これから作るファイルを指定する用途なので、上書き確認は出しません。
            // BUIは書込み前に必ずバックアップを取るため、ここで警告しても二重になります。
            OverwritePrompt = false,
            AddExtension = true,
            DefaultExt = ".csv"
        }, initialPath, cancellationToken);

    /// <summary>既存のフォルダを選びます。GoldStandardのデータフォルダ用です。</summary>
    public async Task<string?> PickExistingFolderAsync(
        string title, string? initialPath, CancellationToken cancellationToken)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        string? selected = null;
        // ダイアログは必ずUIスレッドで開きます。親を指定しないとRevitの背面に隠れます。
        await _ui.InvokeAsync(() =>
        {
            using var owner = CreateOwnerWindow();
            var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (accepted == true) selected = dialog.FolderName;
        }, cancellationToken).ConfigureAwait(false);

        return selected;
    }

    private async Task<string?> ShowAsync(
        Microsoft.Win32.FileDialog dialog, string? initialPath, CancellationToken cancellationToken)
    {
        ApplyInitialLocation(dialog, initialPath);

        string? selected = null;
        // ダイアログは必ずUIスレッドで開きます。
        await _ui.InvokeAsync(() =>
        {
            // 親ウィンドウを指定しないと、ダイアログがRevitの背面に隠れて
            // 「ボタンを押しても何も起きない」ように見えます（Revitを閉じると現れる）。
            // RevitはWPFアプリではないため System.Windows.Application.Current は null になり、
            // そこから親ウィンドウを得ることはできません。
            // 代わりに、このプロセス（Revit.exe）のメインウィンドウを親にします。
            using var owner = CreateOwnerWindow();
            var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (accepted == true) selected = dialog.FileName;
        }, cancellationToken).ConfigureAwait(false);

        return selected;
    }

    /// <summary>
    /// Revitのメインウィンドウを親に持つ、画面には出さない仲介ウィンドウを作ります。
    /// WPFのダイアログはWindowしか親に取れないため、ウィンドウハンドルを橋渡しします。
    /// 親を用意できない場合はnullを返し、ダイアログは親なしで開きます。
    /// </summary>
    private static OwnerWindow? CreateOwnerWindow()
    {
        try
        {
            var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (handle == IntPtr.Zero) return null;

            var owner = new OwnerWindow();
            // 表示せずにハンドルだけを作り、その親をRevitのウィンドウに設定します。
            new System.Windows.Interop.WindowInteropHelper(owner) { Owner = handle }.EnsureHandle();
            return owner;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>画面には出さず、親子関係を作るためだけに使う空のウィンドウです。</summary>
    private sealed class OwnerWindow : Window, IDisposable
    {
        public OwnerWindow()
        {
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;
            Width = 0;
            Height = 0;
            Visibility = Visibility.Hidden;
        }

        public void Dispose() => Close();
    }

    /// <summary>前回の入力値があれば、そのフォルダーとファイル名を初期表示にします。</summary>
    private static void ApplyInitialLocation(Microsoft.Win32.FileDialog dialog, string? initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath)) return;

        try
        {
            var directory = Path.GetDirectoryName(initialPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                dialog.InitialDirectory = directory;

            var fileName = Path.GetFileName(initialPath);
            if (!string.IsNullOrWhiteSpace(fileName)) dialog.FileName = fileName;
        }
        catch (ArgumentException)
        {
            // 入力途中の不正なパスでダイアログを開けなくしないため、初期表示だけ諦めます。
        }
    }
}
