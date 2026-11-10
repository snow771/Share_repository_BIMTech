// このファイルは、Revit ExternalEventの予約が失敗結果を返す場合と例外を投げる場合を、同じCSV補償経路へ流すために存在します。
// OperationResultとAssemblyPersistenceCoordinatorに依存し、境界変更は新規Assembly行の残留や二重削除に影響します。
using Bui.Common;

namespace Bui.Assemblies;

/// <summary>イベント予約結果と、予約失敗時のCSV補償結果を一組で返します。</summary>
public sealed record AssemblyEnqueueOutcome(
    OperationResult<bool> EnqueueResult,
    AssemblyPersistenceCompletion? PersistenceCompletion);

/// <summary>予約例外を失敗結果へ変換し、要求無効化後に一度だけCSV補償します。</summary>
public static class AssemblyEnqueueBoundary
{
    public static Task<AssemblyEnqueueOutcome> EnqueueAsync(
        Func<OperationResult<bool>> enqueue,
        Action cancelBeforeCompensation,
        AssemblyPersistenceCoordinator persistence)
    {
        ArgumentNullException.ThrowIfNull(enqueue);
        ArgumentNullException.ThrowIfNull(cancelBeforeCompensation);
        ArgumentNullException.ThrowIfNull(persistence);

        OperationResult<bool> enqueueResult;
        try
        {
            enqueueResult = enqueue() ?? OperationResult<bool>.Failure(
                "ASSEMBLY_EVENT_ENQUEUE_FAILED",
                "Revit処理の予約結果を取得できませんでした。",
                "The enqueue delegate returned null.");
        }
        catch (Exception exception)
        {
            enqueueResult = OperationResult<bool>.Failure(
                "ASSEMBLY_EVENT_ENQUEUE_THROWN",
                "Revit処理の予約中に例外が発生したため、書き込みを中止しました。",
                exception.ToString());
        }

        if (enqueueResult.IsSuccess)
        {
            return Task.FromResult(new AssemblyEnqueueOutcome(enqueueResult, null));
        }

        var failureDetails = new List<string>
        {
            $"{enqueueResult.Error?.Code}: {enqueueResult.Error?.TechnicalDetail}"
        };
        try
        {
            // Raiseが例外を投げる直前に要求を受理していた可能性があるため、awaitより前に必ず無効化します。
            cancelBeforeCompensation();
        }
        catch (Exception cancelException)
        {
            // 無効化通知が失敗しても、新規CSV行の補償は飛ばしません。
            failureDetails.Add($"Cancel-before-compensation failed: {cancelException}");
        }

        return CompleteFailureAsync(enqueueResult, persistence, failureDetails);
    }

    private static async Task<AssemblyEnqueueOutcome> CompleteFailureAsync(
        OperationResult<bool> enqueueResult,
        AssemblyPersistenceCoordinator persistence,
        IReadOnlyList<string> failureDetails)
    {
        // CSV実装の同期読取り部分もRevit/WPF UIスレッド外で始めます。
        var completion = await Task
            .Run(() => persistence.CompleteAsync(
                revitWriteCommitted: false,
                failureDetails))
            .ConfigureAwait(false);
        return new AssemblyEnqueueOutcome(enqueueResult, completion);
    }
}
