// このファイルは利用者がリボンの「ID初期設定」を押したときだけ共有ID項目の登録を実行し、日本語で結果を表示します。
// Revit IExternalCommand、SharedParameterService、ParameterBindingReportに依存し、戻り値や表示文を変えると初期設定時の利用者判断に影響します。
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Application;
using Bui.Common;
using Bui.Configuration;
using Bui.Products;

namespace Bui.Revit.SharedParameters;

/// <summary>Product_IDとAssembly_IDの安全な初期設定を明示操作で実行します。</summary>
[Transaction(TransactionMode.Manual)]
public sealed class InstallIdentifiersCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        // 原因が特定できている失敗は、汎用の「予期しない問題」に丸めず、そのまま画面へ出します。
        // 会社PCでしか再現できないため、理由が画面に出ないと1往復で1つしか原因を潰せません。
        string? knownReason = null;
        var result = BuiRuntimeSafety.Run<object>("INSTALL_IDENTIFIERS", () =>
        {
            try
            {
                var document = commandData.Application.ActiveUIDocument?.Document
                    ?? throw new IdentifierSetupException("初期設定するRevitプロジェクトを開いてください。");
                var report = new SharedParameterService().Install(
                    document,
                    commandData.Application.Application);

                ShowReport(report);
                return (object)true;
            }
            catch (IdentifierSetupException exception)
            {
                knownReason = exception.Message;
                return (object)false;
            }
        });

        var reason = knownReason ?? (result.IsSuccess ? null : result.Error!.UserMessage);
        if (reason is not null)
        {
            message = $"BUI ID初期設定に失敗しました。\n{reason}";
            TaskDialog.Show(
                "BUI ID初期設定",
                "初期設定を完了できませんでした。\n"
                + "プロジェクトへの途中の変更は取り消しています。\n\n"
                + reason);
            return Result.Failed;
        }

        return Result.Succeeded;
    }

    private static void ShowReport(ParameterBindingReport report)
    {
        var added = report.Categories.Count(result => result.Status == "追加");
        var alreadyCorrect = report.Categories.Count(result => result.Status == "設定済み");
        var skipped = report.Categories.Count(result => result.Status == "対象外");
        var unchanged = report.Categories.Count(result => result.Status == "未変更");
        var hasConflicts = report.Conflicts.Count > 0;

        // 全一覧はファイルへ書き出します。画面へ入れると描画されずRevitが固まります
        // （2026-09-10に発生。ParameterBindingReportTextの冒頭コメント参照）。
        var savedFilePath = SaveDetailedReport(report);

        var dialog = new TaskDialog("BUI ID初期設定")
        {
            MainInstruction = hasConflicts
                ? "初期設定は完了しましたが、確認が必要な競合があります。"
                : "Product_IDとAssembly_IDの初期設定が完了しました。",
            MainContent =
                $"追加: {added} カテゴリ\n"
                + $"設定済み: {alreadyCorrect} カテゴリ\n"
                + $"対象外: {skipped} カテゴリ\n"
                + $"競合により未変更: {unchanged} カテゴリ\n\n"
                + (hasConflicts
                    ? "競合した既存項目とその値は変更していません。"
                    : "対象外のカテゴリはRevitの制約により登録していません。"),
            // 競合と保存先だけを出します。行数の上限はParameterBindingReportTextが持ちます。
            // ここで長い文字列を渡すと、ダイアログは作られるのに描画されず、
            // Escでしか抜けられない状態になります。
            ExpandedContent = DisplayWidthWrapper.Wrap(
                ParameterBindingReportText.BuildDialogDetail(report, savedFilePath)),
            MainIcon = hasConflicts
                ? TaskDialogIcon.TaskDialogIconWarning
                : TaskDialogIcon.TaskDialogIconInformation,
            CommonButtons = TaskDialogCommonButtons.Close
        };

        dialog.Show();
    }

    /// <summary>
    /// カテゴリごとの全一覧をログと同じフォルダーへ書き出し、そのパスを返します。
    /// 書けなかった場合はnullを返します。ここで例外を投げると、
    /// 登録は成功しているのに「失敗しました」と表示されてしまいます。
    /// </summary>
    private static string? SaveDetailedReport(ParameterBindingReport report)
    {
        try
        {
            var folder = BuiLocalPaths.Logs;
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, ParameterBindingReportText.BuildFileName(DateTime.Now));
            // Revitやメモ帳が文字化けせず開けるよう、BOM付きUTF-8で書きます。
            File.WriteAllText(path, ParameterBindingReportText.BuildFileReport(report), new UTF8Encoding(true));
            return path;
        }
        catch (Exception)
        {
            // 保存先やパスは例外メッセージに混じり得るため、画面へは出しません。
            // 保存できなかったことはBuildDialogDetailが画面に書きます。
            return null;
        }
    }
}
