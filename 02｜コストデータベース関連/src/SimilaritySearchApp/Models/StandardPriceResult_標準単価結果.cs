namespace SimilaritySearchApp.Models;

/// <summary>
/// 工種コード1つ分の標準単価のまとまり。
/// 実績（査定・修正）は「補正後 × 表示係数（工事原価モード）」の値。
/// </summary>
/// <param name="WorkTypeCode">工種コード</param>
/// <param name="Trade">工種項目（実績データから引いた名称。無ければ null）</param>
/// <param name="Work">工事項目</param>
/// <param name="Item">項目</param>
/// <param name="SourceSheet">建築材料マスタの元シート名（名称が引けないときの見出し）</param>
/// <param name="ProjectAssessAmount">選択物件の、この工種コードの査定額（円）</param>
/// <param name="ProjectReviseAmount">選択物件の、この工種コードの修正額（円）</param>
/// <param name="ProjectAssessPerM2">選択物件の査定額 ÷ 基準面積（円/㎡）</param>
/// <param name="ProjectRevisePerM2">選択物件の修正額 ÷ 基準面積（円/㎡）</param>
/// <param name="MedianRevisePerM2">絞込後の物件群での、この工種コードの修正単価（円/㎡）中央値</param>
/// <param name="SampleCount">中央値の母数（物件数）</param>
public sealed record StandardPriceGroup(
    string WorkTypeCode, string? Trade, string? Work, string? Item, string? SourceSheet,
    double? ProjectAssessAmount, double? ProjectReviseAmount,
    double? ProjectAssessPerM2, double? ProjectRevisePerM2,
    double? MedianRevisePerM2, int SampleCount,
    List<MaterialRecord> Materials);

/// <summary>【標準単価】タブに表示する内容。</summary>
public sealed class StandardPriceResult
{
    /// <summary>建築材料マスタを読み込めているか</summary>
    public bool Available { get; set; }

    /// <summary>利用者への補足（読込失敗・該当なしの理由など）</summary>
    public string? Message { get; set; }

    /// <summary>表示範囲の説明（例: 工種:内装工事 / 物件 J00123 の工種コード）</summary>
    public string ScopeLabel { get; set; } = "";

    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }

    public int MaterialCount { get; set; }
    public int GroupCount { get; set; }

    public List<StandardPriceGroup> Groups { get; set; } = new();
}
