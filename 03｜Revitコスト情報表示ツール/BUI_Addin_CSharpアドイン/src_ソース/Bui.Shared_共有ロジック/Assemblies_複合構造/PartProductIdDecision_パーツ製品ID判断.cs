// このファイルはPartのProduct_IDを原則として材料から導出し、明示確認済みのPart個別値だけを上書きとして認めるために存在します。
// PartMaterialReaderとOperationResultに依存され、判断変更は原価集計のProduct_IDと監査記録に影響します。
using Bui.Common;

namespace Bui.Assemblies;

public sealed record PartProductIdOverrideAudit(
    long PartElementId,
    string MaterialProductId,
    string PartProductId,
    bool ExplicitlyConfirmed);

public sealed record PartProductIdResolution(
    string EffectiveProductId,
    bool WasPartOverride,
    PartProductIdOverrideAudit? OverrideAudit);

/// <summary>Part個別値が材料値と競合する場合の確認と監査規則を適用します。</summary>
public static class PartProductIdDecision
{
    public static OperationResult<PartProductIdResolution> Resolve(
        string? materialProductId,
        string? partProductId,
        bool overrideConfirmed,
        long partElementId)
    {
        var materialValue = materialProductId?.Trim() ?? string.Empty;
        var partValue = partProductId?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(partValue))
        {
            return string.IsNullOrEmpty(materialValue)
                ? OperationResult<PartProductIdResolution>.Failure(
                    "PART_PRODUCT_ID_MISSING",
                    "Partの材料にProduct_IDを設定してください。",
                    $"Part {partElementId}: material Product_ID is blank.")
                : OperationResult<PartProductIdResolution>.Success(
                    new PartProductIdResolution(materialValue, false, null));
        }

        if (string.Equals(materialValue, partValue, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<PartProductIdResolution>.Success(
                new PartProductIdResolution(materialValue, false, null));
        }

        if (!overrideConfirmed)
        {
            return OperationResult<PartProductIdResolution>.Failure(
                "PART_PRODUCT_ID_OVERRIDE_CONFIRMATION_REQUIRED",
                "Part個別のProduct_IDを使用するには明示確認が必要です。",
                $"Part {partElementId}: material='{materialValue}', part='{partValue}'.");
        }

        return OperationResult<PartProductIdResolution>.Success(new PartProductIdResolution(
            partValue,
            WasPartOverride: true,
            new PartProductIdOverrideAudit(partElementId, materialValue, partValue, ExplicitlyConfirmed: true)));
    }
}
