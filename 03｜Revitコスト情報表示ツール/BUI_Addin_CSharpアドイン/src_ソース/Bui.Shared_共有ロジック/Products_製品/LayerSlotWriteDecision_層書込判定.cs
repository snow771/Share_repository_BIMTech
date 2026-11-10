// このファイルの目的:
//   層1スロットへ書き込んでよいかを、既存値・選ぼうとしている値・上書き確認から判定します。
//
// なぜ必要か:
//   層の行は、まだ触っていない層でもアンカー要素の現在値で初期化されます（Task 7）。
//   「同一タイプすべて」で適用すると、その値がプレビューを見た要素だけでなく、
//   異なる値を持つすべての同タイプ要素へ無条件に上書きされてしまいます。
//   プレビュー側は既にこの状態を検知して確認を求めますが（LayerTargetSurvey.HasConflictingExistingValue）、
//   利用者の答えを実際の書込みで守るのはこの判定です。
//
// なぜRevitを参照しない場所に置くのか:
//   BulkAssignmentPlan / ProductIdEditabilityDecisionと同じく、
//   Revitが無い環境でも分類規則をテストで固定するためです。
//
// 修正時の影響:
//   ここを緩めると、確認していない値が黙って上書きされます。
namespace Bui.Products;

/// <summary>層1スロットの既存値と選択値を比べた結果です。</summary>
public enum LayerSlotWriteOutcome
{
    /// <summary>既存値が空、または既存値と選択値が同じ。安全に書けます。</summary>
    Write,

    /// <summary>既存値と選択値が既に同じ。書き直す必要がありません。</summary>
    AlreadySame,

    /// <summary>既存値が選択値と異なります。上書き確認が無ければ書いてはいけません。</summary>
    Conflict
}

/// <summary>層1スロットへ書き込むかどうかの純粋な判定です。</summary>
public static class LayerSlotWriteDecision
{
    /// <summary>既存値と選択値だけから分類します。上書き確認の有無はここでは見ません。</summary>
    public static LayerSlotWriteOutcome Classify(string? currentValue, string targetValue)
    {
        var current = (currentValue ?? string.Empty).Trim();
        var target = (targetValue ?? string.Empty).Trim();

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            return LayerSlotWriteOutcome.AlreadySame;
        }

        return current.Length == 0 ? LayerSlotWriteOutcome.Write : LayerSlotWriteOutcome.Conflict;
    }

    /// <summary>
    /// 実際に書いてよいかを返します。競合（既存値が異なる）は、
    /// 利用者が上書きを明示的に確認した場合だけ書きます。
    /// </summary>
    public static bool ShouldWrite(string? currentValue, string targetValue, bool conflictOverwriteConfirmed) =>
        Classify(currentValue, targetValue) switch
        {
            LayerSlotWriteOutcome.Write => true,
            LayerSlotWriteOutcome.Conflict => conflictOverwriteConfirmed,
            _ => false
        };
}
