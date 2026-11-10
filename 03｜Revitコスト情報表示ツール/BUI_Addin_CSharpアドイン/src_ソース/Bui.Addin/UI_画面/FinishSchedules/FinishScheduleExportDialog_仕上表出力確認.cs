// このファイルの目的:
//   仕上表出力の直前に、件数と警告をTaskDialogで確認させ、続行が確認できたら
//   SaveFileDialogで保存先を選ばせます。Revit/WPFのAPI呼び出しだけをここに置きます。
//
// なぜExpandedContentの文字列をここで組み立てないのか:
//   2026-09-10、TaskDialog.ExpandedContentへ行数上限を検証していない文字列を渡した結果、
//   ダイアログが作られるのに描画されず、Revitが操作不能になった事故がありました
//   (Bui.Products.ParameterBindingReportTextの冒頭コメント参照)。
//   このマシンではBui.Addinをビルドできず、ここに書いた文字列組み立てのロジックは
//   実機でしか検証できません。行数上限という安全条件は、ビルド・テストできる
//   Bui.Shared側のBui.FinishSchedules.FinishScheduleDialogTextへ寄せ、
//   ここではその戻り値をそのままMainContent/ExpandedContentへ渡すだけにします。
//
// なぜSaveFileDialogにRevitのウィンドウハンドルを親として渡すのか:
//   RevitはWPFアプリではないため、System.Windows.Application.Currentからは
//   親ウィンドウを取れません。親を指定しないとダイアログがRevitの背面に隠れ、
//   「押しても何も起きない」ように見えます(Bui.UI.WpfFilePickerServiceの同じ対処を参照)。
//   橋渡し用の非表示ウィンドウを作り、そのOwnerへRevitのウィンドウハンドルを設定します。
//
// 修正時の影響:
//   ExpandedContentへ渡す文字列をここで直接連結しないでください。
//   行数上限の検証が効かなくなり、同じ事故が仕上表出力でも再発します。

using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;
using Bui.Application;
using Bui.FinishSchedules;

namespace Bui.UI.FinishSchedules;

/// <summary>仕上表出力の事前確認(TaskDialog)と保存先選択(SaveFileDialog)を行います。</summary>
internal sealed class FinishScheduleExportDialog
{
    /// <summary>
    /// 内容を確認するTaskDialogを表示します。「OK」を押したときだけtrueを返し、
    /// 「キャンセル」や右上の×で閉じた場合はfalseを返します。
    /// </summary>
    public bool ConfirmExport(FinishSchedulePreparedResult prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        var hasWarnings = prepared.Output.Warnings.Count > 0;
        var dialog = new TaskDialog("BUI 仕上表出力")
        {
            MainInstruction = "仕上表をExcelへ出力します。よろしければ「OK」を押してください。",
            MainContent = FinishScheduleDialogText.BuildMainContent(
                prepared.InternalRoomCount,
                prepared.ExternalTypeCount,
                prepared.MissingProductIdCount,
                prepared.AffectedRoomCount,
                prepared.AffectedExternalTypeCount,
                prepared.Output.Warnings.Count),
            // 行数上限を守った文字列だけを渡す。ここで文字列を継ぎ足さない(ファイル冒頭コメント参照)。
            ExpandedContent = FinishScheduleDialogText.BuildExpandedContent(prepared.Output.Warnings),
            MainIcon = hasWarnings ? TaskDialogIcon.TaskDialogIconWarning : TaskDialogIcon.TaskDialogIconInformation,
            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
        };

        return dialog.Show() == TaskDialogResult.Ok;
    }

    /// <summary>
    /// 保存先の.xlsxを選ばせます。キャンセル時はnullを返し、呼出元はファイルを作りません。
    /// </summary>
    public string? PickSaveFilePath(string documentTitle, IntPtr ownerHandle)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "仕上表の保存先",
            Filter = "Excelブック (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{SafeFileName(documentTitle)}_仕上表_{DateTime.Now:yyyyMMdd}.xlsx"
        };

        using var owner = CreateOwnerWindow(ownerHandle);
        var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return accepted == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Revitのメインウィンドウを親に持つ、画面には出さない仲介ウィンドウを作ります。
    /// WPFのダイアログはWindowしか親に取れないため、ウィンドウハンドルを橋渡しします。
    /// 親を用意できない場合はnullを返し、ダイアログは親なしで開きます。
    /// </summary>
    private static OwnerWindow? CreateOwnerWindow(IntPtr ownerHandle)
    {
        if (ownerHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var owner = new OwnerWindow();
            new WindowInteropHelper(owner) { Owner = ownerHandle }.EnsureHandle();
            return owner;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Windowsのファイル名として使えない文字を置き換えます。</summary>
    private static string SafeFileName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "無題";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var characters = title.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(characters);
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
}
