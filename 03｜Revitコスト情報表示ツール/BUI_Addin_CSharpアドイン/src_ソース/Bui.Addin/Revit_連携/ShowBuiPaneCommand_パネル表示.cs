// このファイルはリボンの「BUIパネル」ボタンから登録済みドッキングパネルを表示します。
// Revit UI APIと固定DockablePaneIdに依存し、GUIDやコマンド署名を変えると既存パネルを開けなくなります。
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Application;
using Bui.Resources;

namespace Bui.Revit;

/// <summary>非表示のBUIドッキングパネルを表示します。</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ShowBuiPaneCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        var result = BuiRuntimeSafety.Run<object>("SHOW_BUI_PANE", () =>
        {
            // 起動時に登録した同じ固定GUIDでパネルを取得し、閉じている場合も再表示します。
            var paneId = new DockablePaneId(Identifiers.DockablePaneId);
            commandData.Application.GetDockablePane(paneId).Show();
            return true;
        });
        if (!result.IsSuccess)
        {
            message = $"BUIパネルを表示できませんでした。\n{result.Error!.UserMessage}";
            return Result.Failed;
        }
        return Result.Succeeded;
    }
}
