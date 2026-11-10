// このファイルは、共有ViewModelの変更をWPF画面スレッドへ戻す唯一のアダプターです。
// WPF Dispatcherに依存し、変更すると非同期検索後のバインド更新に影響します。
using System.Windows.Threading;

namespace Bui.UI;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public async Task InvokeAsync(Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await _dispatcher.InvokeAsync(action, DispatcherPriority.DataBind, cancellationToken).Task.ConfigureAwait(false);
    }
}
