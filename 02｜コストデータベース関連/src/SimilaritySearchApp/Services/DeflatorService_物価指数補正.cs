using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>
/// 物価指数補正（Python の apply_deflator / mean_deflator / _assign_term）。
/// 工種コード → カテゴリーコード → 指数割当 → (term, 指数割当) → deflator の順に引く。
/// 各マスタはキーごとに先頭の1件だけを使う（pandas の drop_duplicates と同じ）。
/// </summary>
public static class DeflatorService
{
    public static void Apply(
        IList<CostRecord> costs,
        IReadOnlyList<CategoryRow> categories,
        IReadOnlyList<IndexAllocationRow> indexAllocations,
        IReadOnlyList<DeflatorRow> deflators)
    {
        var catByCode = new Dictionary<string, int?>();
        foreach (var c in categories)
            catByCode.TryAdd(c.WorkTypeCode.Trim(), c.CategoryCode);

        var iaByCategory = new Dictionary<int, string>();
        foreach (var r in indexAllocations)
            if (r.CategoryCode is int code)
                iaByCategory.TryAdd(code, r.IndexAllocation.Trim());

        var defByKey = new Dictionary<(string Term, string Ia), double?>();
        foreach (var d in deflators)
            defByKey.TryAdd((d.Term.Trim(), d.IndexAllocation.Trim()), d.Deflator);

        foreach (var cost in costs)
        {
            double? value = null;
            if (catByCode.TryGetValue(cost.WorkTypeCode.Trim(), out var cat) && cat is int catCode
                && iaByCategory.TryGetValue(catCode, out var ia)
                && defByKey.TryGetValue((cost.Term.Trim(), ia), out var dv))
            {
                value = dv;
            }

            // 欠損・非有限・0以下は 1.0
            var deflator = value is double v && double.IsFinite(v) && v > 0 ? v : 1.0;
            cost.Deflator = deflator;
            cost.AmountAdj = cost.Amount * deflator;
        }
    }

    /// <summary>補正なし（マスタ読込失敗時）。</summary>
    public static void ApplyNone(IList<CostRecord> costs)
    {
        foreach (var c in costs)
        {
            c.Deflator = 1.0;
            c.AmountAdj = c.Amount;
        }
    }

    /// <summary>竣工日が start ≤ 日付 ≤ end に入る最初の term。該当なしは空文字。</summary>
    public static string AssignTerm(DateTime? completion, IReadOnlyList<TermRow> terms)
    {
        if (completion is not DateTime d) return "";
        foreach (var t in terms)
        {
            if (t.Start is DateTime s && t.End is DateTime e && s <= d && d <= e)
                return t.Term.Trim();
        }
        return "";
    }

    /// <summary>工種条件で絞った明細の deflator 平均。対象が無ければ null。</summary>
    public static double? MeanDeflator(IEnumerable<CostRecord> costs, string trade, string work, string item)
    {
        var values = UnitPriceService.FilterByTrade(costs, trade, work, item)
            .Select(c => c.Deflator)
            .Where(double.IsFinite)
            .ToList();
        return values.Count == 0 ? null : values.Average();
    }
}
