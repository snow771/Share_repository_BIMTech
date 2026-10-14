namespace SimilaritySearchApp.Models;

/// <summary>
/// 査定単価／修正単価の明細1行。
/// <see cref="Amount"/> は補正前、<see cref="AmountAdj"/> は物価指数補正後（Amount × Deflator）。
/// </summary>
public sealed class CostRecord
{
    public string ProjectId { get; set; } = "";

    /// <summary>工種コード（例: 02-04-10000）</summary>
    public string WorkTypeCode { get; set; } = "";

    /// <summary>工種項目</summary>
    public string Trade { get; set; } = "";

    /// <summary>工事項目</summary>
    public string Work { get; set; } = "";

    /// <summary>項目</summary>
    public string Item { get; set; } = "";

    /// <summary>金額（円）補正前</summary>
    public double Amount { get; set; }

    /// <summary>物価指数補正係数。補正なしは 1.0。</summary>
    public double Deflator { get; set; } = 1.0;

    /// <summary>金額（円）補正後</summary>
    public double AmountAdj { get; set; }

    /// <summary>物件の竣工日から求めた期間</summary>
    public string Term { get; set; } = "";

    public double GetAmount(bool adjusted) => adjusted ? AmountAdj : Amount;
}
