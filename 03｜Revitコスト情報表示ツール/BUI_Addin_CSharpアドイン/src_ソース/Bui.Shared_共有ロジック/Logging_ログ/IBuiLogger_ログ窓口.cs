// このファイルはログ保存先を呼出側から切り離す最小契約です。
// LogEntryに依存し、変更はすべてのエラー保護とテストに影響します。
namespace Bui.Logging;

public interface IBuiLogger
{
    Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default);
}
