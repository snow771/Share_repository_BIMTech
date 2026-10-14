using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>統計計算。パーセンタイルは numpy.percentile の既定（linear）と同じ値を返す。</summary>
public static class StatisticsService
{
    /// <summary>昇順ソート済みの数列のパーセンタイル（p: 0〜100）。空なら NaN。</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return double.NaN;
        if (sorted.Count == 1) return sorted[0];
        var rank = (sorted.Count - 1) * Math.Clamp(p, 0, 100) / 100.0;
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    /// <summary>有限値だけを昇順で返す。</summary>
    public static List<double> SortedFinite(IEnumerable<double> values)
    {
        var list = values.Where(double.IsFinite).ToList();
        list.Sort();
        return list;
    }

    /// <summary>件数・Q1・中央値・Q3（Python の summarize_range）。</summary>
    public static RangeSummary Summarize(IEnumerable<double> values)
    {
        var s = SortedFinite(values);
        if (s.Count == 0) return RangeSummary.Empty;
        return new RangeSummary
        {
            N = s.Count,
            Q1 = Percentile(s, 25),
            Median = Percentile(s, 50),
            Q3 = Percentile(s, 75),
        };
    }

    public static double? Median(IEnumerable<double> values)
    {
        var s = SortedFinite(values);
        return s.Count == 0 ? null : Percentile(s, 50);
    }
}
