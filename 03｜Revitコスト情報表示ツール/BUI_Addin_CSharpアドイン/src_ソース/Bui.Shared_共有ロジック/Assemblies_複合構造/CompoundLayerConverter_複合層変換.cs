// このファイルはRevit読取スナップショットを、構成CSVと照合が使うAssemblyLayerRecordへ安全に変換するために存在します。
// CompoundLayerSnapshot、AssemblyLayerRecord、OperationResultに依存し、必須条件の変更は自動登録の停止条件に影響します。
using Bui.Common;

namespace Bui.Assemblies;

/// <summary>Revitの順序を変えずに、登録可能な構成層へ変換します。</summary>
public static class CompoundLayerConverter
{
    public static OperationResult<IReadOnlyList<AssemblyLayerRecord>> ToAssemblyLayers(
        IEnumerable<CompoundLayerSnapshot> snapshots,
        bool isVerticallyCompound = false)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        if (isVerticallyCompound)
        {
            return OperationResult<IReadOnlyList<AssemblyLayerRecord>>.Failure(
                "ASSEMBLY_REVIEW_REQUIRED",
                "垂直複合構造は形状を手動確認してください。",
                "CompoundStructure.IsVerticallyCompound=true; automatic registration is disabled.");
        }

        var layers = new List<AssemblyLayerRecord>();
        foreach (var snapshot in snapshots)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (string.IsNullOrWhiteSpace(snapshot.ProductId))
            {
                return OperationResult<IReadOnlyList<AssemblyLayerRecord>>.Failure(
                    "ASSEMBLY_PRODUCT_ID_MISSING",
                    "すべての構成層の材料にProduct_IDを設定してください。",
                    $"レイヤー{snapshot.Order + 1}（内部の層順序 {snapshot.Order}） / 材料「{snapshot.MaterialName}」のProduct_IDが空です。");
            }

            // GetLayersが保証するRevit順序のまま追加し、表示用材料名は照合条件に混ぜません。
            layers.Add(new AssemblyLayerRecord(
                snapshot.Order,
                snapshot.Function,
                snapshot.ThicknessMillimeters,
                snapshot.ProductId.Trim(),
                snapshot.MaterialName));
        }

        return OperationResult<IReadOnlyList<AssemblyLayerRecord>>.Success(layers);
    }
}
