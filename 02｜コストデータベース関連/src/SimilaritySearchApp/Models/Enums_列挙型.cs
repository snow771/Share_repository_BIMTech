namespace SimilaritySearchApp.Models;

/// <summary>構造種別。RC造は project_id が J、S造は I で始まる。</summary>
public enum StructureType
{
    RC,
    S,
}

/// <summary>単価算出の基準面積。</summary>
public enum BasisMode
{
    /// <summary>法廷床面積</summary>
    LegalFloorArea,
    /// <summary>施工床面積</summary>
    ConstructionFloorArea,
}

/// <summary>費用の表示モード。</summary>
public enum CostMode
{
    /// <summary>純工事費（そのまま）</summary>
    NetConstruction,
    /// <summary>工事原価（純工事費 ÷ 0.705）</summary>
    ConstructionCost,
}

public static class EnumLabels
{
    public static string ToLabel(this BasisMode m) =>
        m == BasisMode.LegalFloorArea ? "法廷床面積" : "施工床面積";

    public static string ToLabel(this CostMode m) =>
        m == CostMode.NetConstruction ? "純工事費" : "工事原価";

    public static string ToLabel(this StructureType s) =>
        s == StructureType.RC ? "RC造(J*)" : "S造(I*)";

    public static string ProjectIdPrefix(this StructureType s) =>
        s == StructureType.RC ? "J" : "I";

    public static BasisMode? ParseBasisMode(string? label) => label switch
    {
        "法廷床面積" => BasisMode.LegalFloorArea,
        "施工床面積" => BasisMode.ConstructionFloorArea,
        _ => null,
    };

    public static CostMode? ParseCostMode(string? label) => label switch
    {
        "純工事費" => CostMode.NetConstruction,
        "工事原価" => CostMode.ConstructionCost,
        _ => null,
    };
}
