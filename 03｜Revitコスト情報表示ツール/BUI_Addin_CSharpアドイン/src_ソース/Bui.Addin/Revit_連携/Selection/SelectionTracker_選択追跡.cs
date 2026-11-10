// このファイルはRevitの選択変更を読取り専用で監視し、軽量な選択状態だけを画面モデルへ渡します。
// Revit UIイベントとISelectionSnapshotSinkに依存し、処理追加は選択操作の応答性とRevitのイベント制約に影響します。
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Bui.UI;

namespace Bui.Revit.Selection;

/// <summary>SelectionChangedを同じハンドラーで登録・解除し、Revit書込みを行わず選択状態だけを通知します。</summary>
public sealed class SelectionTracker
{
    private readonly ISelectionSnapshotSink _sink;
    private bool _isSubscribed;

    public SelectionTracker(ISelectionSnapshotSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void Subscribe(UIControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (_isSubscribed)
        {
            return;
        }

        application.SelectionChanged += OnSelectionChanged;
        _isSubscribed = true;
    }

    public void Unsubscribe(UIControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!_isSubscribed)
        {
            return;
        }

        application.SelectionChanged -= OnSelectionChanged;
        _isSubscribed = false;
    }

    /// <summary>文書またはビューの切替後に、以前の数値ElementIdを必ず破棄します。</summary>
    public void OnViewActivated(object? sender, ViewActivatedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        // ElementIdはDocumentの内側でだけ意味を持つため、別Documentへ持ち越してはいけません。
        // 新ビューの現在選択を推測せず、次のSelectionChangedが届くまで空選択を表示する方が安全です。
        var document = eventArgs.Document;
        _sink.Post(new SelectionSnapshot(
            HasDocument: document is not null,
            ElementIds: Array.Empty<long>(),
            ContainsLinkedElement: false,
            IsReadOnlyContext: document?.IsReadOnly ?? true));
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        // このコールバックではRevitが明示的に許可する選択読取りだけを行います。
        // Transaction、選択変更、CSV/LLM処理、ドキュメント書込みは絶対に追加しません。
        var document = eventArgs.GetDocument();
        var elementIds = eventArgs.GetSelectedElements()
            .Select(elementId => elementId.Value)
            .OrderBy(value => value)
            .ToArray();
        var containsLinkedElement = eventArgs.GetReferences()
            .Any(reference => reference.LinkedElementId != ElementId.InvalidElementId);

        _sink.Post(new SelectionSnapshot(
            HasDocument: document is not null,
            ElementIds: elementIds,
            ContainsLinkedElement: containsLinkedElement,
            IsReadOnlyContext: document?.IsReadOnly ?? true));
    }
}
