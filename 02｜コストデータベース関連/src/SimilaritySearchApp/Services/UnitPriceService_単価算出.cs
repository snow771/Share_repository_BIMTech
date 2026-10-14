using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>物件ごとの単価1点（合計金額 ÷ 基準面積）。</summary>
public sealed record UnitPricePoint(string ProjectId, double Area, double SumAmount, double YenPerM2);

/// <summary>単価（円/㎡）の算出（Python の build_unit_series）。</summary>
public static class UnitPriceService
{
    /// <summary>
    /// 工種・工事項目・項目で明細を絞り、物件ごとに合計して基準面積で割る。
    /// 面積が 0 以下、または合計が 0 以下の物件は除外する。並び順は projects の順。
    /// </summary>
    public static List<UnitPricePoint> Build(
        IReadOnlyList<ProjectRecord> projects,
        IEnumerable<CostRecord> costs,
        BasisMode basis,
        string trade, string work, string item,
        bool adjusted)
    {
        var sums = new Dictionary<string, double>();
        foreach (var c in FilterByTrade(costs, trade, work, item))
        {
            sums[c.ProjectId] = sums.TryGetValue(c.ProjectId, out var s)
                ? s + c.GetAmount(adjusted)
                : c.GetAmount(adjusted);
        }

        var result = new List<UnitPricePoint>();
        foreach (var p in projects)
        {
            if (!sums.TryGetValue(p.ProjectId, out var sum)) continue;
            if (p.GetArea(basis) is not double area || !(area > 0)) continue;
            if (!(sum > 0)) continue;
            result.Add(new UnitPricePoint(p.ProjectId, area, sum, sum / area));
        }
        return result;
    }

    /// <summary>工種項目・工事項目・項目の完全一致で絞る（未指定は条件なし）。</summary>
    public static IEnumerable<CostRecord> FilterByTrade(IEnumerable<CostRecord> costs, string trade, string work, string item)
    {
        var q = costs;
        if (FilterCriteria.IsSet(trade)) q = q.Where(c => c.Trade == trade);
        if (FilterCriteria.IsSet(work)) q = q.Where(c => c.Work == work);
        if (FilterCriteria.IsSet(item)) q = q.Where(c => c.Item == item);
        return q;
    }

    /// <summary>物件ID → 明細一覧の索引。</summary>
    internal static Dictionary<string, List<CostRecord>> GroupByProject(IEnumerable<CostRecord> costs)
    {
        var map = new Dictionary<string, List<CostRecord>>();
        foreach (var c in costs)
        {
            var key = c.ProjectId.Trim();
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<CostRecord>();
            list.Add(c);
        }
        return map;
    }
}
