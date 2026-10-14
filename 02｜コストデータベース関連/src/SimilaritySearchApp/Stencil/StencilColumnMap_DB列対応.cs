using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Stencil;

/// <summary>
/// Stencile の出力列（日本語）→ DB の列名（英字）の対応。
/// 物件情報は ProjectRepository の SELECT と同じ対応。単価は査定/修正単価テーブルの列。
/// 実テーブルの列名は「Project_id」でも「Project_id_現場コード」（ER 図の論理名）でも見つかるように解決する。
/// </summary>
public static class StencilColumnMap
{
    /// <summary>出力列 → ProjectInformation の列（「間取」は DB に列が無いので対象外）</summary>
    public static readonly IReadOnlyDictionary<string, string> ProjectColumns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["project_id"] = "Project_id",
        ["物件名"] = "ProjectName",
        ["現場住所_都道府県"] = "SiteAddress_State_Province",
        ["現場住所_市区町村"] = "SiteAddress_City_Town_Village",
        ["現場住所_町名"] = "SiteAddress_StreetName",
        ["請負金額"] = "ContractAmount",
        ["契約時予定利益"] = "ProjectedProfitatContractSigning",
        ["利益率"] = "ProfitMargin",
        ["敷地面積"] = "SiteArea",
        ["建築面積"] = "BuildingFootprint",
        ["法廷床面積"] = "LegalFloorArea",
        ["施工床面積"] = "ConstructionFloorArea",
        ["施工坪単価"] = "ConstructionTsubo",
        ["工期(着工)"] = "ConstructionPeriod_Start",
        ["工期(竣工)"] = "ConstructionPeriod_Completion",
        ["構造"] = "Structure",
        ["階数_地上"] = "NumberofFloors_AboveGround",
        ["階数_地下"] = "NumberofFloors_BelowGround",
        ["1R"] = "1R", ["1K"] = "1K", ["1DK"] = "1DK", ["1LDK"] = "1LDK",
        ["2K"] = "2K", ["2DK"] = "2DK", ["2LDK"] = "2LDK",
        ["3K"] = "3K", ["3DK"] = "3DK", ["3LDK"] = "3LDK",
        ["4K"] = "4K", ["4DK"] = "4DK", ["4LDK"] = "4LDK",
        ["5K"] = "5K", ["5DK"] = "5DK", ["5LDK"] = "5LDK",
        ["1SK"] = "1SK", ["2SK"] = "2SK", ["１SLDK"] = "1SLDK", ["2SLDK"] = "2SLDK", ["3SLDK"] = "3SLDK",
        ["OR"] = "OR",
        ["テナント"] = "Tenant",
        ["店舗"] = "Store",
        ["事務所"] = "Office",
        ["その他"] = "Other",
        ["世帯数"] = "NumberofHouseholds",
        ["形態"] = "Type",
        ["ELV"] = "ELV",
        ["ｺﾝｸﾘｰﾄ"] = "Concrete",
        ["型枠"] = "Formwork",
        ["鉄筋（Kg/m2）"] = "ReinforcingBars",
        ["鉄骨（Kg/m2）"] = "SteelFrame",
        ["ＡＬＣ"] = "ALC",
        ["外壁タイル"] = "ExteriorWallTiles",
        ["山留工法"] = "RetainingWallConstruction",
        ["杭工事"] = "PileWorkTruck",
        ["土工事"] = "EarthworkTruck",
        ["型枠・鉄筋"] = "Formwork_ReinforcingBarsTruck",
        ["鉄骨"] = "SteelFrameTruck",
        ["ALC"] = "ALCTruck",
        ["生ｺﾝ車"] = "Ready_MixConcreteTruck",
        ["ﾎﾟﾝﾌﾟ車"] = "PumpTruck",
    };

    // 単価テーブルの列（英字の基本名）
    public const string CostProjectId = "Project_id";
    public const string CostWorkTypeCode = "Work_Type_Code";
    public const string CostTrade = "Work_Type_Item";
    public const string CostWork = "Construction_Item";
    public const string CostItem = "Project";
    public const string AssessPrice = "Appraisal_Unit_Price";
    public const string RevisePrice = "Revised_Unit_Price";

    /// <summary>
    /// 基本名に合う実際の列。完全一致（大文字小文字は無視）→「基本名_日本語…」の順。
    /// 「Formwork」が「Formwork_ReinforcingBarsTruck」に誤って当たらないよう、接尾辞は日本語で始まるものだけを認める。
    /// </summary>
    public static DbColumnInfo? Resolve(IReadOnlyList<DbColumnInfo> columns, string baseName)
    {
        var exact = columns.FirstOrDefault(c => string.Equals(c.Name, baseName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var prefix = baseName + "_";
        return columns.FirstOrDefault(c =>
            c.Name.Length > prefix.Length &&
            c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            c.Name[prefix.Length] > 0x7F);
    }
}
