// このファイルはモデルレス画面からRevit APIコンテキストへ渡す処理を、1件だけ待機できるExternalEventに集約します。
// Revit ExternalEvent、SinglePendingRequest、OperationResultに依存し、待機・解放規則の変更は要求の重複実行や消失に影響します。
using Autodesk.Revit.UI;
using Bui.Application;
using Bui.Common;
using Bui.UI;

namespace Bui.Revit.ExternalEvents;

/// <summary>Revit APIコンテキストで実行する一回分の処理です。</summary>
public interface IRevitActionRequest
{
    string Name { get; }

    void Execute(UIApplication application);
}

/// <summary>一つのExternalEventと一つの待機要求を所有する仲介役です。</summary>
public sealed class RevitActionDispatcher : IExternalEventHandler, IDisposable
{
    private readonly SinglePendingRequest<IRevitActionRequest> _pending = new();
    private readonly ExternalEvent _externalEvent;
    private bool _isDisposed;

    public RevitActionDispatcher()
    {
        _externalEvent = ExternalEvent.Create(this);
    }

    public OperationResult<bool> Enqueue(IRevitActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_isDisposed)
        {
            return OperationResult<bool>.Failure(
                "REVIT-ACTION-DISPOSED",
                "BUIを終了中のため、Revit処理を開始できません。",
                request.Name);
        }

        return _pending.TrySetAndRaise(request, () =>
        {
            var raiseResult = _externalEvent.Raise();
            if (raiseResult == ExternalEventRequest.Accepted)
            {
                return OperationResult<bool>.Success(true);
            }

            // 未受理とRaise例外のどちらでも、共有スロットがこの要求だけを安全に解放します。
            return OperationResult<bool>.Failure(
                "REVIT-ACTION-RAISE-FAILED",
                "Revit処理を予約できませんでした。現在の操作を終えてからもう一度お試しください。",
                $"{request.Name}: ExternalEventRequest={raiseResult}");
        });
    }

    public void Execute(UIApplication application)
    {
        var request = _pending.Current;
        if (request is null)
        {
            return;
        }

        try
        {
            var result = BuiRuntimeSafety.Run<object>("REVIT_EXTERNAL_EVENT", () =>
            {
                request.Execute(application);
                return true;
            });
            if (!result.IsSuccess)
            {
                TaskDialog.Show("BUI", $"Revit処理「{request.Name}」を完了できませんでした。\n{result.Error!.UserMessage}");
            }
        }
        finally
        {
            // 成功・利用者エラー・例外のすべてで解放し、次の要求が永久にbusyにならないようにします。
            _pending.ClearIfCurrent(request);
        }
    }

    public string GetName() => "BUI Revit Action Dispatcher";

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _externalEvent.Dispose();
    }
}
