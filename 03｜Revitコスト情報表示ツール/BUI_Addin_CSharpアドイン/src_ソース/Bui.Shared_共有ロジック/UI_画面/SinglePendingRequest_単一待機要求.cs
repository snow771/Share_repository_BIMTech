// このファイルはExternal Eventで実行待ちにできる要求を1件に制限し、二重受付時に先行要求を保護します。
// OperationResultと参照型要求のみに依存し、待機・解放規則の変更はRevit操作の消失、置換、重複実行に影響します。
using Bui.Common;

namespace Bui.UI;

/// <summary>スレッド間で安全に1件だけ保持する待機要求スロットです。</summary>
public sealed class SinglePendingRequest<TRequest>
    where TRequest : class
{
    private readonly object syncRoot = new();
    private TRequest? current;

    public TRequest? Current
    {
        get
        {
            lock (syncRoot)
            {
                return current;
            }
        }
    }

    /// <summary>空きがある場合だけ要求を保持し、先行要求は絶対に置き換えません。</summary>
    public OperationResult<bool> TrySet(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (syncRoot)
        {
            if (current is not null)
            {
                return OperationResult<bool>.Failure(
                    "REVIT-ACTION-BUSY",
                    "別のRevit処理中です。完了後にもう一度お試しください。",
                    $"待機中の要求型: {typeof(TRequest).FullName}");
            }

            current = request;
            return OperationResult<bool>.Success(true);
        }
    }

    /// <summary>要求を保持してから予約し、予約失敗・例外時は自分が保持した要求だけを解放します。</summary>
    public OperationResult<bool> TrySetAndRaise(
        TRequest request,
        Func<OperationResult<bool>> raise)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(raise);

        var stored = TrySet(request);
        if (!stored.IsSuccess)
        {
            return stored;
        }

        try
        {
            // 受理成功時はExternalEvent実行完了までスロットを保持します。
            var raised = raise() ?? OperationResult<bool>.Failure(
                "REVIT-ACTION-RAISE-FAILED",
                "Revit処理の予約結果を取得できませんでした。",
                "The raise delegate returned null.");
            if (raised.IsSuccess)
            {
                return raised;
            }

            ClearIfCurrent(request);
            return raised;
        }
        catch
        {
            // Raise例外は呼出元で詳細を報告できるよう再送出し、busy残留だけを防ぎます。
            ClearIfCurrent(request);
            throw;
        }
    }

    /// <summary>指定要求が現在の所有者と同一参照の場合だけ解放します。</summary>
    public bool ClearIfCurrent(TRequest expectedOwner)
    {
        ArgumentNullException.ThrowIfNull(expectedOwner);

        lock (syncRoot)
        {
            if (!ReferenceEquals(current, expectedOwner))
            {
                return false;
            }

            current = null;
            return true;
        }
    }

    /// <summary>実行のfinally節から呼び出し、成功・失敗のどちらでも次の要求を受付可能にします。</summary>
    public void Clear()
    {
        lock (syncRoot)
        {
            current = null;
        }
    }
}
