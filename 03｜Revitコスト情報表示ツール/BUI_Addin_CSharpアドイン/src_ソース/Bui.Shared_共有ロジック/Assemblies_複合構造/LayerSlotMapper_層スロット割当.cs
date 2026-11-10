// このファイルの目的:
//   複合構造の層の並びを、6つの箱へ割り当てます。
//
// 規則:
//   室内側の層が「仕上」。そこから外側へ向かって 下地①→⑤。
//   7層以上ある場合は、室内側から6層目までを割り当て、残りは割り当てません。
//   割り当てなかった層は捨てずにOverflowへ入れ、画面で知らせます。
//
// なぜRevitに聞かないのか:
//   Revitの層順は、壁なら外部側から、床なら上面から始まります。
//   どちら端が室内側かはカテゴリごとに違い、その判定はRevit側の仕事です。
//   ここは「どちら端か」だけを受け取り、割当の規則だけを持ちます。
//   こうすることでRevit無しで検証できます。
//
// 修正時の影響:
//   割当がずれると、仕上のIDが下地の欄に書き込まれます。
//   モデルを見ても間違いに気づけないため、必ずテストで固定してください。

namespace Bui.Assemblies;

/// <summary>1つの層と、その層が入る箱の組です。</summary>
public sealed record LayerSlotAssignment(AssemblyLayerRecord Layer, LayerSlot Slot);

/// <summary>割当の結果です。溢れた層と、その説明を含みます。</summary>
public sealed record LayerSlotPlan(
    IReadOnlyList<LayerSlotAssignment> Assigned,
    IReadOnlyList<AssemblyLayerRecord> Overflow,
    string Warning);

/// <summary>層の並びを箱へ割り当てる純粋関数です。</summary>
public static class LayerSlotMapper
{
    public static LayerSlotPlan Map(IReadOnlyList<AssemblyLayerRecord> layers, InteriorSide interiorSide)
    {
        ArgumentNullException.ThrowIfNull(layers);

        // 室内側から外側への並びに揃えます。以降はこの順で数えるだけです。
        var fromInterior = interiorSide == InteriorSide.LastLayer
            ? layers.Reverse().ToArray()
            : layers.ToArray();

        var assigned = fromInterior
            .Take(LayerSlotNames.Count)
            .Select((layer, index) => new LayerSlotAssignment(layer, (LayerSlot)index))
            .ToArray();

        var overflow = fromInterior.Skip(LayerSlotNames.Count).ToArray();
        var warning = overflow.Length == 0
            ? string.Empty
            : $"層が{fromInterior.Length}あります。室内側から{LayerSlotNames.Count}層目までに付与し、"
                + $"外側の{overflow.Length}層には付与しません。";

        return new LayerSlotPlan(assigned, overflow, warning);
    }
}
