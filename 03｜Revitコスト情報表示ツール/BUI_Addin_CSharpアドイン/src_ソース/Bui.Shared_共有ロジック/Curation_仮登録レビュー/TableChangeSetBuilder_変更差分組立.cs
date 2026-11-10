// このファイルの目的:
//   DB内容更新画面で、Excelライクなグリッド上での編集・削除を、
//   保存前の確認ダイアログに出す「変更一覧」へ組み立てます。
//   SQL接続を一切持たない、純粋な差分抽出ロジックです。

namespace Bui.Curation;

/// <summary>1セルの変更です。</summary>
public sealed record CellChange(string PrimaryKeyValue, string Column, string OldValue, string NewValue);

/// <summary>1回の保存操作でSQL Serverへ反映する変更一式です。</summary>
public sealed record ChangeSummary(IReadOnlyList<CellChange> Updates, IReadOnlyList<string> DeletedPrimaryKeyValues);

/// <summary>元の行一覧と、編集後の行一覧を突き合わせ、変更一覧を組み立てます。</summary>
public static class TableChangeSetBuilder
{
    public static ChangeSummary Build(
        IReadOnlyList<IReadOnlyDictionary<string, string>> originalRows,
        IReadOnlyList<IReadOnlyDictionary<string, string>> currentRows,
        IReadOnlySet<string> deletedPrimaryKeyValues,
        string primaryKeyColumn)
    {
        ArgumentNullException.ThrowIfNull(originalRows);
        ArgumentNullException.ThrowIfNull(currentRows);
        ArgumentNullException.ThrowIfNull(deletedPrimaryKeyValues);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKeyColumn);

        var originalByKey = originalRows.ToDictionary(row => row[primaryKeyColumn], row => row, StringComparer.Ordinal);
        var updates = new List<CellChange>();

        foreach (var currentRow in currentRows)
        {
            var key = currentRow[primaryKeyColumn];
            if (deletedPrimaryKeyValues.Contains(key)) continue;
            if (!originalByKey.TryGetValue(key, out var originalRow)) continue;

            foreach (var pair in currentRow)
            {
                if (pair.Key == primaryKeyColumn) continue;
                var oldValue = originalRow.TryGetValue(pair.Key, out var value) ? value : string.Empty;
                if (!string.Equals(oldValue, pair.Value, StringComparison.Ordinal))
                {
                    updates.Add(new CellChange(key, pair.Key, oldValue, pair.Value));
                }
            }
        }

        return new ChangeSummary(updates, deletedPrimaryKeyValues.ToArray());
    }
}

/// <summary>削除予定の行に対し、子表からの参照件数をもとに警告文を組み立てます。</summary>
public static class DeletionWarningBuilder
{
    /// <summary>全件数が0なら警告は不要としてnullを返します。</summary>
    public static string? BuildWarning(string primaryKeyValue, IReadOnlyList<(string ChildTable, int Count)> childCounts)
    {
        var nonZero = childCounts.Where(entry => entry.Count > 0).ToArray();
        if (nonZero.Length == 0) return null;

        var detail = string.Join("、", nonZero.Select(entry => $"{entry.ChildTable}: {entry.Count}件"));
        return $"「{primaryKeyValue}」は次の表から参照されています。削除すると参照エラーになる可能性があります。{detail}";
    }
}
