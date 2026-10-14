namespace SimilaritySearchApp.Models;

/// <summary>数列の統計要約。データが無い値は null（JSON で扱いやすくするため NaN を使わない）。</summary>
public sealed class RangeSummary
{
    public int N { get; init; }
    /// <summary>下位25%</summary>
    public double? Q1 { get; init; }
    public double? Median { get; init; }
    /// <summary>上位25%</summary>
    public double? Q3 { get; init; }

    public static RangeSummary Empty { get; } = new();

    /// <summary>表示係数（工事原価モードなど）を掛けた要約を返す。</summary>
    public RangeSummary Scale(double factor) => new()
    {
        N = N,
        Q1 = Q1 * factor,
        Median = Median * factor,
        Q3 = Q3 * factor,
    };
}
