// このファイルの目的:
//   リボンの「AIに聞く」からチャットウィンドウを開きます。
//
// なぜウィンドウを使い回すのか:
//   二度押すたびに新しいウィンドウが増えると、どれが最新か分からなくなります。
//   同じウィンドウを前面に出し、選択中の情報だけ読み直します。
//
// 修正時の影響:
//   OwnerにRevitのメインウィンドウを設定しないと、裏へ回り込んで見失います。

using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Application;
using Bui.Configuration;
using Bui.Data;
using Bui.Llm;
using Bui.UI;

namespace Bui.Revit;

/// <summary>AIチャットの別ウィンドウを開きます。</summary>
[Transaction(TransactionMode.Manual)]
public sealed class AskBuiAiCommand : IExternalCommand
{
    private static BuiChatWindow? _window;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var result = BuiRuntimeSafety.Run<object>("ASK_BUI_AI", () =>
        {
            var pane = BuiSession.PaneViewModel
                ?? throw new InvalidOperationException(
                    "BUIパネルがまだ準備できていません。リボンの「BUIパネル」を一度開いてから、もう一度お試しください。");

            if (_window is null)
            {
                _window = new BuiChatWindow(BuiChatComposition.Create(pane));
                // Revitのメインウィンドウを親にし、裏へ回り込まないようにします。
                new WindowInteropHelper(_window).Owner =
                    commandData.Application.MainWindowHandle;
            }

            _window.ShowRefreshed();
            return true;
        });

        if (!result.IsSuccess)
        {
            message = $"AIチャットを開けませんでした。\n{result.Error!.UserMessage}";
            TaskDialog.Show("BUI AIチャット", result.Error.UserMessage);
            return Result.Failed;
        }

        return Result.Succeeded;
    }

    /// <summary>
    /// アドイン終了時（BuiApplication.CleanupOwnedResources）に呼びます。
    /// OnClosingは常に隠すだけなので、ここでForceCloseを呼ばないと誰もこのウィンドウを
    /// 閉じられず、Disposeされたパネルへ束縛されたまま残ります。
    /// </summary>
    internal static void CloseWindow()
    {
        _window?.ForceClose();
        _window = null;
    }
}
