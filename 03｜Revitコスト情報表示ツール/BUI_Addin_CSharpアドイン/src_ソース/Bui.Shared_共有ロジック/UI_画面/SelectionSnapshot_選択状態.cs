// このファイルはRevitの選択変更をUI層へ安全に渡す最小の不変状態と受信契約を定義します。
// Revit APIに依存せずlong要素IDと状態値のみを持ち、形を変えると選択追跡・パネル表示・将来のTask 10 UIに影響します。
namespace Bui.UI;

/// <summary>Revitドキュメントと現在選択の軽量スナップショットです。</summary>
public sealed record SelectionSnapshot(
    bool HasDocument,
    IReadOnlyList<long> ElementIds,
    bool ContainsLinkedElement,
    bool IsReadOnlyContext);

/// <summary>選択スナップショットを画面モデルへ配布するRevit非依存の窓口です。</summary>
public interface ISelectionSnapshotSink
{
    void Post(SelectionSnapshot snapshot);
}

/// <summary>画面が参照する最新の選択状態を保持し、変更時だけ購読者へ通知します。</summary>
public sealed class SelectionSnapshotViewModel : ISelectionSnapshotSink
{
    public SelectionSnapshot Current { get; private set; } = new(
        HasDocument: false,
        ElementIds: Array.Empty<long>(),
        ContainsLinkedElement: false,
        IsReadOnlyContext: false);

    public event Action<SelectionSnapshot>? SnapshotChanged;

    public void Post(SelectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Current = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }
}
