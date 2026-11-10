// このファイルは選択された壁・床・天井・屋根のタイプから、Revit順序の複合層を読み取るために存在します。
// Revit 2025 DB API、固定GUID、CompoundLayerSnapshot、LayerSlotMapperに依存し、材料フォールバック変更はAssembly_ID登録可否に影響します。
// 各層のProduct_IDは材料のパラメータではなく、選択要素（ホストインスタンス）の層別Product_IDパラメータから読み取ります。
// 旧・単一Product_ID（569e8df1-…）はBUIが読み書きしなくなったため、ここでは読みません。
// どの層がどの層別パラメータに対応するかは、書込み側と同じLayerSlotMapperで判定します（食い違うと表示と書込み先がずれます）。
using Autodesk.Revit.DB;
using Bui.Assemblies;
using Bui.Common;
using Bui.Resources;

namespace Bui.Revit.Assemblies;

/// <summary>選択ホストとタイプの識別子、構成順序の層、室内側がどちら端かを保持します。</summary>
public sealed record CompoundStructureReadResult(
    long HostElementId,
    long HostTypeElementId,
    string HostCategoryIdentity,
    string HostCategoryDisplayName,
    bool IsVerticallyCompound,
    IReadOnlyList<CompoundLayerSnapshot> Layers,
    /// <summary>
    /// Layersの並びのうち、どちら端が室内側かです。
    /// 「仕上」をどの層に割り当てるかがこれで決まります（設計 §3）。
    /// </summary>
    InteriorSide InteriorSide);

internal sealed record ResolvedLayerMaterial(Material? Material, bool IsFallback, string FallbackSource);

/// <summary>HostObjAttributes.GetCompoundStructureの結果を共有スナップショットへ変換します。</summary>
public sealed class CompoundStructureReader
{
    public OperationResult<CompoundStructureReadResult> Read(Document document, Element selectedElement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedElement);

        // ElementIdは文書ごとに重複し得るため、引数のDocumentと要素所有元が違う場合は読み取りを中止します。
        // Revit APIのDocumentは同じ文書でも別のラッパーが返ることがあるため、
        // 参照比較（ReferenceEquals）では同一文書でも不一致と誤判定します。等価比較を使います。
        if (!document.Equals(selectedElement.Document))
        {
            return OperationResult<CompoundStructureReadResult>.Failure(
                "REVIT-DOCUMENT-MISMATCH",
                "選択要素と読み取り対象のRevitドキュメントが一致しません。",
                $"Element {selectedElement.Id.Value}: elementDocument={selectedElement.Document.Title}; requestedDocument={document.Title}");
        }

        if (selectedElement is not HostObject host
            || selectedElement is not (Wall or Floor or Ceiling or RoofBase))
        {
            return OperationResult<CompoundStructureReadResult>.Failure(
                "ASSEMBLY_HOST_NOT_SUPPORTED",
                "壁・床・天井・屋根を1つ選択してください。",
                $"Element {selectedElement.Id.Value}: {selectedElement.GetType().FullName}");
        }

        var typeId = host.GetTypeId();
        if (typeId == ElementId.InvalidElementId
            || document.GetElement(typeId) is not HostObjAttributes hostType)
        {
            return OperationResult<CompoundStructureReadResult>.Failure(
                "ASSEMBLY_HOST_TYPE_NOT_FOUND",
                "選択要素のホストタイプを読み取れません。",
                $"Element {selectedElement.Id.Value}: type={typeId.Value}");
        }

        var compound = hostType.GetCompoundStructure();
        if (compound is null)
        {
            return OperationResult<CompoundStructureReadResult>.Failure(
                "ASSEMBLY_COMPOUND_STRUCTURE_NOT_FOUND",
                "選択要素のタイプに複合構造がありません。",
                $"Host type {typeId.Value}: {hostType.Name}");
        }

        var rawSnapshots = compound.GetLayers()
            .Select((layer, index) => CreateSnapshot(document, hostType, layer, index))
            .ToArray();
        var interiorSide = ResolveInteriorSide(selectedElement);
        var snapshots = ApplyElementLayerProductIds(selectedElement, rawSnapshots, interiorSide);
        var category = selectedElement.Category ?? hostType.Category;
        if (category is null || category.Id == ElementId.InvalidElementId || category.Id.Value >= 0)
        {
            return OperationResult<CompoundStructureReadResult>.Failure(
                "ASSEMBLY_HOST_CATEGORY_UNSTABLE",
                "ホストカテゴリの安定IDを読み取れないため、Assembly_IDを自動照合できません。",
                $"Element {selectedElement.Id.Value}: category={category?.Name ?? "<missing>"}, id={category?.Id.Value}");
        }

        var categoryIdentity = HostCategoryIdentity.FromBuiltInCategoryId(
            category.Id.Value,
            category.Name);
        return OperationResult<CompoundStructureReadResult>.Success(new CompoundStructureReadResult(
            selectedElement.Id.Value,
            typeId.Value,
            categoryIdentity.FingerprintKey,
            categoryIdentity.DisplayName,
            compound.IsVerticallyCompound,
            snapshots,
            interiorSide));
    }

    /// <summary>
    /// 各層のProduct_IDを、材料ではなく選択要素（ホストインスタンス）の層別パラメータから読み直します。
    /// どの層がどのパラメータへ入るかは、書込み側（LayerAssignmentReader.MapLayers）と同じ
    /// LayerSlotMapperで決めます。6箱に収まらなかった層（Overflow）には何も割り当てないため、
    /// そこは常に空文字です（設計上、書込み側も付与しません）。
    /// </summary>
    private static CompoundLayerSnapshot[] ApplyElementLayerProductIds(
        Element selectedElement,
        IReadOnlyList<CompoundLayerSnapshot> snapshots,
        InteriorSide interiorSide)
    {
        var layersForMapping = snapshots
            .Select(snapshot => new AssemblyLayerRecord(
                snapshot.Order,
                snapshot.Function,
                snapshot.ThicknessMillimeters,
                string.Empty,
                snapshot.MaterialName,
                snapshot.MaterialElementId,
                snapshot.IsMaterialFallback))
            .ToArray();
        var plan = LayerSlotMapper.Map(layersForMapping, interiorSide);
        var slotByOrder = plan.Assigned.ToDictionary(
            assignment => assignment.Layer.Order,
            assignment => assignment.Slot);

        return snapshots
            .Select(snapshot => snapshot with
            {
                ProductId = slotByOrder.TryGetValue(snapshot.Order, out var slot)
                    ? ReadElementLayerProductId(selectedElement, slot)
                    : string.Empty
            })
            .ToArray();
    }

    private static string ReadElementLayerProductId(Element element, LayerSlot slot)
    {
        var index = (int)slot;
        if (index < 0 || index >= Identifiers.LayerProductIdParameterGuids.Count) return string.Empty;
        return element.get_Parameter(Identifiers.LayerProductIdParameterGuids[index])?.AsString()?.Trim()
            ?? string.Empty;
    }

    /// <summary>
    /// Revitの複合構造の層順は、壁なら外部側から、床なら上面から始まります。
    /// どちら端が室内側かはホストの種類で決まります。
    ///
    /// この対応はRevitが無い環境では検証できません。誤っていると、
    /// 仕上のIDが最も外側の層へ書き込まれ、モデルを見ても気づけません。
    /// そのため画面では必ず「どの層がどのパラメータへ入るか」を先に見せます。
    /// </summary>
    private static InteriorSide ResolveInteriorSide(Element host) => host switch
    {
        Floor => InteriorSide.FirstLayer,
        _ => InteriorSide.LastLayer
    };

    private static CompoundLayerSnapshot CreateSnapshot(
        Document document,
        HostObjAttributes hostType,
        CompoundStructureLayer layer,
        int order)
    {
        var resolved = ResolveMaterial(document, hostType, layer);
        var material = resolved.Material;
        var thickness = UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Millimeters);
        // ProductIdはここでは仮の空文字です。ApplyElementLayerProductIdsが選択要素の
        // 層別パラメータから読み直して差し替えます（材料のパラメータは読みません）。
        return new CompoundLayerSnapshot(
            order,
            layer.Function.ToString(),
            Convert.ToDecimal(thickness),
            material?.Id.Value ?? ElementId.InvalidElementId.Value,
            material?.Name ?? $"<By Category: {resolved.FallbackSource}>",
            string.Empty,
            resolved.IsFallback);
    }

    /// <summary>明示材料がない層は、機能別サブカテゴリ材、ホストカテゴリ材の順で解決します。</summary>
    internal static ResolvedLayerMaterial ResolveMaterial(
        Document document,
        HostObjAttributes hostType,
        CompoundStructureLayer layer)
    {
        var explicitMaterial = document.GetElement(layer.MaterialId) as Material;
        if (explicitMaterial is not null)
        {
            return new ResolvedLayerMaterial(explicitMaterial, false, "Explicit");
        }

        var subcategoryId = GetFunctionSubcategory(hostType, layer.Function);
        if (subcategoryId is BuiltInCategory builtInCategory)
        {
            try
            {
                var subcategoryMaterial = document.Settings.Categories.get_Item(builtInCategory)?.Material;
                if (subcategoryMaterial is not null)
                {
                    return new ResolvedLayerMaterial(
                        subcategoryMaterial,
                        true,
                        builtInCategory.ToString());
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // テンプレートに対応サブカテゴリがない場合は、公式手順どおりホストカテゴリへ進みます。
            }
        }

        return new ResolvedLayerMaterial(
            hostType.Category?.Material,
            true,
            hostType.Category?.Name ?? "Unresolved category");
    }

    private static BuiltInCategory? GetFunctionSubcategory(
        HostObjAttributes hostType,
        MaterialFunctionAssignment function)
    {
        var family = hostType switch
        {
            WallType => 0,
            FloorType => 1,
            CeilingType => 2,
            RoofType => 3,
            _ => -1
        };

        return (family, function) switch
        {
            (0, MaterialFunctionAssignment.Membrane) => BuiltInCategory.OST_WallsMembrane,
            (0, MaterialFunctionAssignment.Structure) => BuiltInCategory.OST_WallsStructure,
            (0, MaterialFunctionAssignment.Insulation) => BuiltInCategory.OST_WallsInsulation,
            (0, MaterialFunctionAssignment.Substrate) => BuiltInCategory.OST_WallsSubstrate,
            (0, MaterialFunctionAssignment.Finish1) => BuiltInCategory.OST_WallsFinish1,
            (0, MaterialFunctionAssignment.Finish2) => BuiltInCategory.OST_WallsFinish2,
            (1, MaterialFunctionAssignment.Membrane) => BuiltInCategory.OST_FloorsMembrane,
            (1, MaterialFunctionAssignment.Structure) => BuiltInCategory.OST_FloorsStructure,
            (1, MaterialFunctionAssignment.Insulation) => BuiltInCategory.OST_FloorsInsulation,
            (1, MaterialFunctionAssignment.Substrate) => BuiltInCategory.OST_FloorsSubstrate,
            (1, MaterialFunctionAssignment.Finish1) => BuiltInCategory.OST_FloorsFinish1,
            (1, MaterialFunctionAssignment.Finish2) => BuiltInCategory.OST_FloorsFinish2,
            (2, MaterialFunctionAssignment.Membrane) => BuiltInCategory.OST_CeilingsMembrane,
            (2, MaterialFunctionAssignment.Structure) => BuiltInCategory.OST_CeilingsStructure,
            (2, MaterialFunctionAssignment.Insulation) => BuiltInCategory.OST_CeilingsInsulation,
            (2, MaterialFunctionAssignment.Substrate) => BuiltInCategory.OST_CeilingsSubstrate,
            (2, MaterialFunctionAssignment.Finish1) => BuiltInCategory.OST_CeilingsFinish1,
            (2, MaterialFunctionAssignment.Finish2) => BuiltInCategory.OST_CeilingsFinish2,
            (3, MaterialFunctionAssignment.Membrane) => BuiltInCategory.OST_RoofsMembrane,
            (3, MaterialFunctionAssignment.Structure) => BuiltInCategory.OST_RoofsStructure,
            (3, MaterialFunctionAssignment.Insulation) => BuiltInCategory.OST_RoofsInsulation,
            (3, MaterialFunctionAssignment.Substrate) => BuiltInCategory.OST_RoofsSubstrate,
            (3, MaterialFunctionAssignment.Finish1) => BuiltInCategory.OST_RoofsFinish1,
            (3, MaterialFunctionAssignment.Finish2) => BuiltInCategory.OST_RoofsFinish2,
            _ => null
        };
    }
}
