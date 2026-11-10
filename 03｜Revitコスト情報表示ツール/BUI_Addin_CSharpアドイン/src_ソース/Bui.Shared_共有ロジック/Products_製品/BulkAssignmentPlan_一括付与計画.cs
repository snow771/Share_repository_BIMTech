// このファイルはProduct_ID一括付与候補を空欄・同値・競合・書込不可に分類し、実行前プレビューをRevit非依存で作ります。
// 候補DTOと文字列IDのみに依存し、分類や競合保護の変更は書込対象・プレビュー件数・Revit Undo内容に影響します。
namespace Bui.Products;

/// <summary>Revitから読み取ったProduct_ID付与候補の最小情報です。</summary>
public sealed record AssignmentCandidate(long ElementId, string? ExistingProductId, bool IsWritable);

/// <summary>一括付与の事前分類と書込対象選択を保持します。</summary>
public sealed class BulkAssignmentPlan
{
    private BulkAssignmentPlan(
        string targetProductId,
        IReadOnlyList<long> toWrite,
        IReadOnlyList<long> alreadySame,
        IReadOnlyList<long> conflicts,
        IReadOnlyList<long> notWritable)
    {
        TargetProductId = targetProductId;
        ToWrite = toWrite;
        AlreadySame = alreadySame;
        Conflicts = conflicts;
        NotWritable = notWritable;
    }

    public string TargetProductId { get; }

    public IReadOnlyList<long> ToWrite { get; }

    public IReadOnlyList<long> AlreadySame { get; }

    public IReadOnlyList<long> Conflicts { get; }

    public IReadOnlyList<long> NotWritable { get; }

    /// <summary>既存値を保護した事前分類を作成します。</summary>
    public static BulkAssignmentPlan Build(
        string targetProductId,
        IEnumerable<AssignmentCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(targetProductId))
        {
            throw new ArgumentException("付与先のProduct_IDは空にできません。", nameof(targetProductId));
        }

        ArgumentNullException.ThrowIfNull(candidates);

        var normalizedTarget = targetProductId.Trim();
        var toWrite = new List<long>();
        var alreadySame = new List<long>();
        var conflicts = new List<long>();
        var notWritable = new List<long>();

        foreach (var candidate in candidates)
        {
            if (!candidate.IsWritable)
            {
                notWritable.Add(candidate.ElementId);
                continue;
            }

            var existing = candidate.ExistingProductId?.Trim();
            if (string.IsNullOrWhiteSpace(existing))
            {
                toWrite.Add(candidate.ElementId);
            }
            else if (string.Equals(existing, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                alreadySame.Add(candidate.ElementId);
            }
            else
            {
                conflicts.Add(candidate.ElementId);
            }
        }

        return new BulkAssignmentPlan(
            normalizedTarget,
            toWrite.AsReadOnly(),
            alreadySame.AsReadOnly(),
            conflicts.AsReadOnly(),
            notWritable.AsReadOnly());
    }

    /// <summary>通常は空欄だけを返し、明示的な二段階確認後だけ競合も含めます。</summary>
    public IReadOnlyList<long> GetWriteElementIds(bool overwriteConflictsConfirmed)
    {
        if (!overwriteConflictsConfirmed)
        {
            return ToWrite;
        }

        return ToWrite.Concat(Conflicts).ToArray();
    }
}
