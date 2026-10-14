using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>仮設工事の物件ごとの値。</summary>
/// <param name="UnitCostPerM2">仮設工事の施工床単価（円/㎡）</param>
/// <param name="RatioPercent">仮設工事歩掛（%）＝ 仮設 ÷ 直接工事費 × 100</param>
public sealed record TemporaryWorkPoint(string ProjectId, double UnitCostPerM2, double RatioPercent);

/// <summary>仮設工事の集計（Python の calculate_temporary_work_stats）。修正単価を使う。</summary>
public static class TemporaryWorkService
{
    /// <summary>仮設工事の工種コード接頭辞</summary>
    public const string TemporaryPrefix = "01";

    /// <summary>直接工事費とみなす工種コード接頭辞</summary>
    public static readonly string[] DirectPrefixes = { "01", "02", "03", "04", "05", "07" };

    public static List<TemporaryWorkPoint> Calculate(
        IReadOnlyList<ProjectRecord> projects, IEnumerable<CostRecord> revise, bool adjusted)
    {
        var byProject = UnitPriceService.GroupByProject(revise);
        var result = new List<TemporaryWorkPoint>();

        foreach (var p in projects)
        {
            if (p.ConstructionFloorArea is not double area || !(area > 0)) continue;
            if (!byProject.TryGetValue(p.ProjectId.Trim(), out var rows) || rows.Count == 0) continue;

            double sumTemp = 0, sumDirect = 0;
            foreach (var r in rows)
            {
                var code = r.WorkTypeCode;
                var amount = r.GetAmount(adjusted);
                if (code.StartsWith(TemporaryPrefix, StringComparison.Ordinal)) sumTemp += amount;
                if (DirectPrefixes.Any(px => code.StartsWith(px, StringComparison.Ordinal))) sumDirect += amount;
            }

            var unitCost = sumTemp / area;
            var ratio = sumDirect > 0 ? sumTemp / sumDirect * 100 : 0;
            result.Add(new TemporaryWorkPoint(p.ProjectId, unitCost, ratio));
        }
        return result;
    }
}
