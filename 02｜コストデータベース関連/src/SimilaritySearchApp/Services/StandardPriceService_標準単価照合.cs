using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>
/// 建築材料マスタ（標準単価）と、査定・修正単価の実績を工種コードで突き合わせる。
///
/// 表示する工種コードの決め方:
///   1. 工種・工事項目・項目で絞っていれば、その条件に合う実績明細の工種コード
///   2. 物件を選択していれば、その物件の実績明細にある工種コード
///   1 と 2 の両方があれば共通部分。どちらも無ければ全件。
/// 同じ工種コードに複数の材料がある（工種コードだけの照合は「材料の一致」ではない）点に注意。
/// </summary>
public static class StandardPriceService
{
    /// <summary>工種コードが空の材料をまとめる見出し</summary>
    public const string NoCodeLabel = "(工種コード未設定)";

    public static StandardPriceResult Build(
        IReadOnlyList<MaterialRecord> materials,
        IReadOnlyList<ProjectRecord> projects,
        IReadOnlyList<CostRecord> assess,
        IReadOnlyList<CostRecord> revise,
        FilterCriteria c,
        ProjectRecord? selected)
    {
        var result = new StandardPriceResult
        {
            Available = materials.Count > 0,
            ProjectId = selected?.ProjectId,
            ProjectName = selected?.Name,
        };
        if (!result.Available)
        {
            result.Message = "建築材料マスタが読み込まれていません（テーブル名は設定 → DB接続 → テーブル名で確認できます）。";
            return result;
        }

        var f = AnalysisService.DisplayFactor(c.CostMode);
        var allCosts = assess.Concat(revise).ToList();

        // ── 表示する工種コードの集合（null は制限なし） ──
        HashSet<string>? codes = null;
        var tradeFiltered = FilterCriteria.IsSet(c.Trade) || FilterCriteria.IsSet(c.Work) || FilterCriteria.IsSet(c.Item);
        if (tradeFiltered)
        {
            codes = UnitPriceService.FilterByTrade(allCosts, c.Trade, c.Work, c.Item)
                .Select(x => x.WorkTypeCode)
                .Where(x => x.Length > 0)
                .ToHashSet();
        }
        if (selected is not null)
        {
            var own = allCosts.Where(x => x.ProjectId == selected.ProjectId)
                .Select(x => x.WorkTypeCode)
                .Where(x => x.Length > 0)
                .ToHashSet();
            if (codes is null) codes = own;
            else codes.IntersectWith(own);
        }

        var hits = codes is null
            ? materials.ToList()
            : materials.Where(m => codes.Contains(m.WorkTypeCode)).ToList();

        // ── 工種コード → 名称（実績明細の最初の出現） ──
        var names = new Dictionary<string, (string Trade, string Work, string Item)>();
        foreach (var x in allCosts)
            if (x.WorkTypeCode.Length > 0) names.TryAdd(x.WorkTypeCode, (x.Trade, x.Work, x.Item));

        var groupCodes = hits.Select(m => m.WorkTypeCode).ToHashSet();

        // ── 絞込後の物件群での、工種コード別 修正単価（円/㎡）の中央値 ──
        var filtered = ProjectFilterService.Apply(projects, c);
        var areaByPid = new Dictionary<string, double>();
        foreach (var p in filtered)
            if (p.GetArea(c.BasisMode) is double a && a > 0) areaByPid.TryAdd(p.ProjectId, a);

        var reviseSums = new Dictionary<(string Code, string Pid), double>();
        foreach (var x in revise)
        {
            if (!groupCodes.Contains(x.WorkTypeCode) || !areaByPid.ContainsKey(x.ProjectId)) continue;
            var key = (x.WorkTypeCode, x.ProjectId);
            reviseSums[key] = reviseSums.TryGetValue(key, out var s) ? s + x.AmountAdj : x.AmountAdj;
        }
        var perM2ByCode = reviseSums
            .Where(kv => kv.Value > 0)
            .GroupBy(kv => kv.Key.Code)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Value / areaByPid[kv.Key.Pid] * f).ToList());

        // ── 選択物件の工種コード別 実績額 ──
        Dictionary<string, double>? projAssess = null, projRevise = null;
        double? projArea = null;
        if (selected is not null)
        {
            projAssess = SumByCode(assess, selected.ProjectId);
            projRevise = SumByCode(revise, selected.ProjectId);
            projArea = selected.GetArea(c.BasisMode) is double a && a > 0 ? a : null;
        }

        foreach (var g in hits.GroupBy(m => m.WorkTypeCode).OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var code = g.Key;
            names.TryGetValue(code, out var name);
            var samples = perM2ByCode.TryGetValue(code, out var list) ? list : new List<double>();

            double? pa = projAssess is not null && projAssess.TryGetValue(code, out var va) ? va * f : null;
            double? pr = projRevise is not null && projRevise.TryGetValue(code, out var vr) ? vr * f : null;

            result.Groups.Add(new StandardPriceGroup(
                code.Length == 0 ? NoCodeLabel : code,
                Blank(name.Trade), Blank(name.Work), Blank(name.Item),
                g.Select(m => m.SourceSheet).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                pa, pr,
                pa / projArea, pr / projArea,
                StatisticsService.Median(samples), samples.Count,
                g.ToList()));
        }

        result.GroupCount = result.Groups.Count;
        result.MaterialCount = hits.Count;
        result.ScopeLabel = ScopeLabel(c, selected, tradeFiltered);
        if (result.GroupCount == 0)
        {
            result.Message = selected is not null
                ? "選択物件の工種コードに該当する標準材料がありません。工種の絞込を緩めるか、物件の選択を解除してください。"
                : "現在の工種条件に該当する標準材料がありません。";
        }
        return result;
    }

    // ------------------------------------------------------------------

    private static Dictionary<string, double> SumByCode(IEnumerable<CostRecord> costs, string projectId)
    {
        var map = new Dictionary<string, double>();
        foreach (var x in costs)
        {
            if (x.ProjectId != projectId || x.WorkTypeCode.Length == 0) continue;
            map[x.WorkTypeCode] = map.TryGetValue(x.WorkTypeCode, out var s) ? s + x.AmountAdj : x.AmountAdj;
        }
        return map;
    }

    private static string ScopeLabel(FilterCriteria c, ProjectRecord? selected, bool tradeFiltered)
    {
        var parts = new List<string>();
        if (tradeFiltered) parts.Add(AnalysisService.SelectionLabel(c));
        if (selected is not null) parts.Add($"物件 {selected.ProjectId} の工種コード");
        return parts.Count == 0 ? "全工種（工種で絞るか物件を選ぶと、該当する標準材料だけになります）" : string.Join(" ∩ ", parts);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
