// このファイルの目的:
//   Assembly_IDを書いてよいかを判定します。
//
// なぜ判定が要るのか:
//   層のProduct_IDは要素ごと、Assembly_IDはタイプごとに付きます。
//   同じタイプの要素AとBがあり、Aだけに層IDを付けた状態でタイプへ番号を振ると、
//   その番号はBの実態と一致しません。番号が嘘になります。
//   また、層のIDが一部だけ埋まっている状態で番号を振ると、
//   残りを埋めた時点で別の番号になり、同じ構成に2つの番号が付きます。
//
// 修正時の影響:
//   採番できない場合でも、層IDの書き込みは止めません。
//   Assembly_IDだけを見送り、理由を画面へ返します。

namespace Bui.Assemblies;

/// <summary>Assembly_IDを付けてよいかと、付けない場合の理由です。</summary>
public sealed record AssemblyIdDecision(bool CanAssign, string Reason);

/// <summary>採番可否の判定です。純粋関数として保ちます。</summary>
public static class AssemblyIdEligibility
{
    /// <param name="layerIdsPerElement">
    /// 同じタイプの要素ごとの、層のProduct_IDの並び（室内側から外側の順）。
    /// </param>
    public static AssemblyIdDecision Evaluate(IReadOnlyList<IReadOnlyList<string>> layerIdsPerElement)
    {
        ArgumentNullException.ThrowIfNull(layerIdsPerElement);

        if (layerIdsPerElement.Count == 0)
        {
            return new AssemblyIdDecision(false, "対象の要素がないため、Assembly_IDは付けませんでした。");
        }

        var normalized = layerIdsPerElement
            .Select(ids => (ids ?? Array.Empty<string>())
                .Select(id => (id ?? string.Empty).Trim())
                .ToArray())
            .ToArray();

        if (normalized.Any(ids => ids.Length == 0))
        {
            return new AssemblyIdDecision(false, "層がないため、Assembly_IDは付けませんでした。");
        }

        if (normalized.Any(ids => ids.Any(id => id.Length == 0)))
        {
            return new AssemblyIdDecision(
                false,
                "層のProduct_IDに空欄があるため、Assembly_IDは付けませんでした。すべての層を埋めると採番されます。");
        }

        var first = normalized[0];
        var allSame = normalized.All(ids =>
            ids.Length == first.Length
            && ids.Zip(first, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase)).All(same => same));

        return allSame
            ? new AssemblyIdDecision(true, string.Empty)
            : new AssemblyIdDecision(
                false,
                "同一タイプ内でProduct_IDが揃っていないため、Assembly_IDは付けませんでした。"
                    + "付与範囲を「同一タイプすべて」にして付け直すと採番されます。");
    }
}
