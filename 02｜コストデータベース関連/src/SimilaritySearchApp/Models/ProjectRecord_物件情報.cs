namespace SimilaritySearchApp.Models;

/// <summary>
/// 物件1件（ProjectInformation_物件情報 の1行）。
/// 計算に使う列は型付きプロパティ、全列は表示用に <see cref="Attributes"/> に日本語列名で保持する。
/// </summary>
public sealed class ProjectRecord
{
    public string ProjectId { get; init; } = "";

    /// <summary>物件名</summary>
    public string? Name { get; init; }

    /// <summary>現場住所_都道府県</summary>
    public string? Prefecture { get; init; }

    /// <summary>現場住所_市区町村</summary>
    public string? City { get; init; }

    /// <summary>現場住所_町名</summary>
    public string? Town { get; init; }

    /// <summary>構造</summary>
    public string? Structure { get; init; }

    /// <summary>形態（数値・英字のみの値は除去済み）</summary>
    public string? Form { get; init; }

    /// <summary>階数_地上</summary>
    public double? FloorsAbove { get; init; }

    /// <summary>階数_地下</summary>
    public double? FloorsBelow { get; init; }

    /// <summary>法廷床面積</summary>
    public double? LegalFloorArea { get; init; }

    /// <summary>施工床面積</summary>
    public double? ConstructionFloorArea { get; init; }

    /// <summary>型枠（㎡）</summary>
    public double? Formwork { get; init; }

    /// <summary>鉄筋（Kg/m2）列の値</summary>
    public double? RebarKgPerM2 { get; init; }

    /// <summary>工期(竣工)</summary>
    public DateTime? CompletionDate { get; init; }

    /// <summary>竣工日から判定した期間（Term_期間マスタ）。該当なしは空文字。</summary>
    public string Term { get; set; } = "";

    /// <summary>全列（日本語列名 → 値）。詳細表示とAIコンテキスト用。</summary>
    public Dictionary<string, object?> Attributes { get; init; } = new();

    public double? GetArea(BasisMode mode) =>
        mode == BasisMode.LegalFloorArea ? LegalFloorArea : ConstructionFloorArea;
}
