// このファイルはRevit Partの材料とProduct_IDを読み、確認済みのPart個別Product_IDだけを監査付き上書きとして解決するために存在します。
// Revit Part.GetMaterialIds、固定GUID、PartProductIdDecisionに依存し、解決規則の変更はPart原価と上書き監査に影響します。
using Autodesk.Revit.DB;
using Bui.Assemblies;
using Bui.Common;
using Bui.Resources;

namespace Bui.Revit.Assemblies;

public sealed record PartMaterialReadResult(
    long PartElementId,
    long MaterialElementId,
    string MaterialName,
    bool IsMaterialFallback,
    string MaterialProductId,
    string PartProductId,
    PartProductIdResolution Resolution);

/// <summary>Partから塗装材を除く材料を一つ解決し、Product_ID判断へ渡します。</summary>
public sealed class PartMaterialReader
{
    public OperationResult<PartMaterialReadResult> Read(Part part, bool partOverrideConfirmed)
    {
        ArgumentNullException.ThrowIfNull(part);
        var document = part.Document;
        var explicitMaterials = part.GetMaterialIds(false)
            .Select(id => document.GetElement(id) as Material)
            .Where(material => material is not null)
            .Cast<Material>()
            .GroupBy(material => material.Id.Value)
            .Select(group => group.First())
            .ToArray();
        if (explicitMaterials.Length > 1)
        {
            return OperationResult<PartMaterialReadResult>.Failure(
                "PART_MATERIAL_AMBIGUOUS",
                "Partに複数の材料があるため、Product_IDを自動判定できません。",
                $"Part {part.Id.Value}: material ids={string.Join(",", explicitMaterials.Select(item => item.Id.Value))}");
        }

        var material = explicitMaterials.SingleOrDefault();
        var isFallback = material is null;
        material ??= part.Category?.Material;
        if (material is null)
        {
            return OperationResult<PartMaterialReadResult>.Failure(
                "PART_MATERIAL_NOT_FOUND",
                "Partの材料を解決できません。",
                $"Part {part.Id.Value}: explicit and category materials are unavailable.");
        }

        var materialProductId = material
            .get_Parameter(Identifiers.ProductIdParameterGuid)
            ?.AsString()
            ?.Trim() ?? string.Empty;
        var partProductId = part
            .get_Parameter(Identifiers.ProductIdParameterGuid)
            ?.AsString()
            ?.Trim() ?? string.Empty;
        var resolution = PartProductIdDecision.Resolve(
            materialProductId,
            partProductId,
            partOverrideConfirmed,
            part.Id.Value);
        if (!resolution.IsSuccess)
        {
            return new OperationResult<PartMaterialReadResult>(default, resolution.Error);
        }

        return OperationResult<PartMaterialReadResult>.Success(new PartMaterialReadResult(
            part.Id.Value,
            material.Id.Value,
            material.Name,
            isFallback,
            materialProductId,
            partProductId,
            resolution.Value!));
    }
}
