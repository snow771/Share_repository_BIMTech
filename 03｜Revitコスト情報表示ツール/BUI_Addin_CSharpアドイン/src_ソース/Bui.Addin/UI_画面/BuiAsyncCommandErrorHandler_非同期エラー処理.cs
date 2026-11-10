// このファイルはWPFコマンド境界の未処理例外を、安全なログへ渡します。
// AsyncCommandとErrorBoundaryに依存し、変更はボタン処理中の例外がRevit UIへ戻るかどうかに影響します。
using Bui.Common;
using Bui.Logging;

namespace Bui.UI;

internal sealed class BuiAsyncCommandErrorHandler(ErrorBoundary boundary) : IAsyncCommandErrorHandler
{
    public void Handle(Exception exception)
    {
        // ErrorBoundaryに同じ例外を捕捉させ、メッセージ中の認証値も秘匿します。
        _ = boundary.Run<object>("WPF_COMMAND", () => throw exception);
    }
}
