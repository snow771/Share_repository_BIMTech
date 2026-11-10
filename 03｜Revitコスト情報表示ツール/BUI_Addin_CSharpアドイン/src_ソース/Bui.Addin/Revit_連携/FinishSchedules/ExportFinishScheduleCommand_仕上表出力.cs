// このファイルの目的:
//   リボンの「仕上表出力」から、部屋とProduct_IDを基にした内部・外部仕上表のExcel出力を起動します。
//
// なぜBuiRuntimeSafety.Runで全体を囲むのか:
//   FinishScheduleComposition.Createが組み立てるcollector/resolver/aggregator/writerの
//   どこかで想定外の例外(RevitFinishSourceCollectorが投げるInvalidOperationException以外の
//   未知の例外)が出ても、Revitの生の例外ダイアログを利用者へ見せないためです
//   (Bui.Revit.SharedParameters.InstallIdentifiersCommandと同じ方針)。
//   FinishScheduleExportController.Run自身は内部で例外を捕まえてErrorMessageへ丸めますが、
//   Run呼び出し前後(BuiSession参照、TaskDialog組み立てなど)の想定外の失敗までは守れないため、
//   Execute全体を境界の内側に置きます。
//
// なぜキャンセルと失敗を別変数で運ぶのか:
//   ErrorBoundary.Runはvoidではなくジェネリック戻り値を要求し、キャンセルも失敗も内部で
//   OperationCanceledExceptionを投げずに正常返却するため(FinishScheduleExportController.Runは
//   利用者取消を例外にしない設計)、ラムダの外へ「取消か」「理由は何か」「出力先はどこか」を
//   明示的なローカル変数で持ち出し、Runの戻り値(OperationResult<T>)だけでは表現しきれない
//   3値(取消/失敗理由/成功時の出力先)を区別します。
//
// 修正時の影響:
//   ここでexception.Message等の生の例外文字列をTaskDialogやmessageへ渡さないでください。
//   内部パスやスタックトレースが利用者の画面に出ます。
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Application;

namespace Bui.Revit.FinishSchedules;

/// <summary>部屋とProduct_IDから内部・外部仕上表をExcelへ出力する、読取専用コマンドです。</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ExportFinishScheduleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var cancelled = false;
        string? knownReason = null;
        string? outputPath = null;

        var result = BuiRuntimeSafety.Run<object>("EXPORT_FINISH_SCHEDULE", () =>
        {
            var document = commandData.Application.ActiveUIDocument?.Document;
            if (document is null)
            {
                TaskDialog.Show("BUI 仕上表出力", "Revitプロジェクトを開いてください。");
                cancelled = true;
                return (object)true;
            }

            var products = BuiSession.ProductRepository;
            if (products is null)
            {
                knownReason = "BUIの製品データが準備されていません。";
                return (object)false;
            }

            var commandResult = FinishScheduleComposition.Create(products)
                .Run(document, commandData.Application.MainWindowHandle);

            if (commandResult.Cancelled)
            {
                cancelled = true;
                return (object)true;
            }

            if (!string.IsNullOrWhiteSpace(commandResult.ErrorMessage))
            {
                knownReason = commandResult.ErrorMessage;
                return (object)false;
            }

            outputPath = commandResult.OutputPath;
            return (object)true;
        });

        if (cancelled)
        {
            return Result.Cancelled;
        }

        var reason = knownReason ?? (result.IsSuccess ? null : result.Error!.UserMessage);
        if (reason is not null)
        {
            message = reason;
            TaskDialog.Show(
                "BUI 仕上表出力",
                "仕上表出力を完了できませんでした。\n\n" + reason);
            return Result.Failed;
        }

        TaskDialog.Show(
            "BUI 仕上表出力",
            "仕上表を出力しました。\n\n" + outputPath);
        return Result.Succeeded;
    }
}
