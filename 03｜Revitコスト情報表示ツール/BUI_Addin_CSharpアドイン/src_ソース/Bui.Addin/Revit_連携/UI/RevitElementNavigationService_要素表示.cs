// このファイルは、点検結果の要素をRevitで選択・表示する読取支援アダプターです。
// Revit ExternalEventに依存し、変更は点検タブのナビゲーションに影響しますがモデル値は書き換えません。
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Revit.ExternalEvents;
using Bui.UI;

namespace Bui.Revit.UI;

public sealed class RevitElementNavigationService : IElementNavigationService
{
    private readonly RevitActionDispatcher _dispatcher;

    public RevitElementNavigationService(RevitActionDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public async Task NavigateAsync(string documentKey, long elementId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKey);
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueue = _dispatcher.Enqueue(new NavigateRequest(documentKey, elementId, completion));
        if (!enqueue.IsSuccess) throw new InvalidOperationException(enqueue.Error!.UserMessage);
        var error = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (error is not null) throw new InvalidOperationException("要素をRevitで表示できませんでした。", error);
    }

    private sealed class NavigateRequest(
        string sourceDocumentKey,
        long elementId,
        TaskCompletionSource<Exception?> completion) : IRevitActionRequest
    {
        public string Name => "点検要素表示";

        public void Execute(UIApplication application)
        {
            try
            {
                var uiDocument = application.ActiveUIDocument
                    ?? throw new InvalidOperationException("開いているRevitドキュメントがありません。");
                if (!string.Equals(RevitDocumentIdentity.GetKey(uiDocument.Document), sourceDocumentKey, StringComparison.Ordinal))
                    throw new InvalidOperationException("点検後にRevit文書が切り替わったため、別文書の同じ要素IDは表示しません。再点検してください。");
                var id = new ElementId(elementId);
                if (uiDocument.Document.GetElement(id) is null) throw new InvalidOperationException($"要素ID {elementId} が見つかりません。");
                uiDocument.Selection.SetElementIds(new[] { id });
                uiDocument.ShowElements(id);
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetResult(exception);
            }
        }
    }
}
