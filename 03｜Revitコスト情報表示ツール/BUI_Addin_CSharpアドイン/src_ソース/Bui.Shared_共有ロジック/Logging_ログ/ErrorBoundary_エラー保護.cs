// このファイルは予期しない例外を利用者向けの安全な日本語結果へ変換します。
// OperationResultとIBuiLoggerに依存し、変更は起動・ファイル・LLMの全エラー表示に影響します。
using Bui.Common;
using System.Text.Json;

namespace Bui.Logging;

public sealed class ErrorBoundary
{
    private readonly IBuiLogger _logger;
    public ErrorBoundary(IBuiLogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public OperationResult<T> Run<T>(string operation, Func<T> action)
    {
        try { return OperationResult<T>.Success(action()); }
        catch (OperationCanceledException) { return Cancelled<T>(); }
        catch (Exception exception)
        {
            TryLogAsync(CreateEntry(operation, exception)).GetAwaiter().GetResult();
            return Unexpected<T>();
        }
    }

    public async Task<OperationResult<T>> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        try { return OperationResult<T>.Success(await action(cancellationToken).ConfigureAwait(false)); }
        catch (OperationCanceledException) { return Cancelled<T>(); }
        catch (Exception exception)
        {
            await TryLogAsync(CreateEntry(operation, exception)).ConfigureAwait(false);
            return Unexpected<T>();
        }
    }

    private static LogEntry CreateEntry(string operation, Exception exception) => new(
        DateTimeOffset.UtcNow, "Error", LogSanitizer.Sanitize(operation, 128), "UNEXPECTED",
        exception.GetType().Name, SafeTechnicalDetail(exception), null);

    private static string SafeTechnicalDetail(Exception exception) => exception switch
    {
        IOException => "An input/output operation failed.",
        UnauthorizedAccessException => "Access to a required resource was denied.",
        HttpRequestException => "A network operation failed.",
        JsonException => "JSON data could not be parsed.",
        _ => "The operation raised an unexpected exception."
    };

    private async Task TryLogAsync(LogEntry entry)
    {
        try { await _logger.WriteAsync(entry, CancellationToken.None).ConfigureAwait(false); } catch { }
    }

    private static OperationResult<T> Unexpected<T>() => OperationResult<T>.Failure("UNEXPECTED",
        "予期しない問題が発生しました。再度実行し、改善しない場合はBUIのログを管理者へお渡しください。", "Unexpected operation failure.");
    private static OperationResult<T> Cancelled<T>() => OperationResult<T>.Failure("CANCELLED", "処理をキャンセルしました。", "Operation cancelled.");
}
