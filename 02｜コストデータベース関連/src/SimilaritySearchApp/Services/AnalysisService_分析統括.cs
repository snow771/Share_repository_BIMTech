using System.Globalization;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>
/// 条件1回分の分析を組み立てる（Python の update_view 相当）。
/// 絞込 → 単価・仮設・型枠/鉄筋の補正前後の集計 → 統計 → グラフ用データ → 物件一覧 → 選択肢。
/// </summary>
public static class AnalysisService
{
    /// <summary>円/㎡ → 円/坪 の係数</summary>
    public const double TsuboPerM2Factor = 3.305785;

    /// <summary>純工事費 → 工事原価 の割戻し係数（工事原価 = 純工事費 ÷ 0.705）</summary>
    public const double PureToGenkaDivisor = 0.705;

    public static double DisplayFactor(CostMode mode) =>
        mode == CostMode.ConstructionCost ? 1.0 / PureToGenkaDivisor : 1.0;

    public static AnalysisResult Analyze(
        IReadOnlyList<ProjectRecord> projects,
        IReadOnlyList<CostRecord> assess,
        IReadOnlyList<CostRecord> revise,
        FilterCriteria criteria)
    {
        var c = criteria.Clone();
        var f = DisplayFactor(c.CostMode);
        var filtered = ProjectFilterService.Apply(projects, c);

        // ── 査定・修正単価（補正前／補正後） ──
        var aRaw = UnitPriceService.Build(filtered, assess, c.BasisMode, c.Trade, c.Work, c.Item, adjusted: false);
        var aAdj = UnitPriceService.Build(filtered, assess, c.BasisMode, c.Trade, c.Work, c.Item, adjusted: true);
        var rRaw = UnitPriceService.Build(filtered, revise, c.BasisMode, c.Trade, c.Work, c.Item, adjusted: false);
        var rAdj = UnitPriceService.Build(filtered, revise, c.BasisMode, c.Trade, c.Work, c.Item, adjusted: true);

        var assessBA = new BeforeAfter(
            StatisticsService.Summarize(aRaw.Select(x => x.YenPerM2)).Scale(f),
            StatisticsService.Summarize(aAdj.Select(x => x.YenPerM2)).Scale(f));
        var reviseBA = new BeforeAfter(
            StatisticsService.Summarize(rRaw.Select(x => x.YenPerM2)).Scale(f),
            StatisticsService.Summarize(rAdj.Select(x => x.YenPerM2)).Scale(f));

        // ── 仮設工事（工種条件に関係なく修正単価全体から算出） ──
        var tRaw = TemporaryWorkService.Calculate(filtered, revise, adjusted: false);
        var tAdj = TemporaryWorkService.Calculate(filtered, revise, adjusted: true);
        var tempUnitBA = new BeforeAfter(
            StatisticsService.Summarize(tRaw.Select(x => x.UnitCostPerM2 * f)),
            StatisticsService.Summarize(tAdj.Select(x => x.UnitCostPerM2 * f)));
        // 歩掛% は比率なので表示係数を掛けない
        var tempRatioBA = new BeforeAfter(
            StatisticsService.Summarize(tRaw.Select(x => x.RatioPercent)),
            StatisticsService.Summarize(tAdj.Select(x => x.RatioPercent)));

        // ── 型枠・鉄筋 ──
        var lRaw = LaborRateService.Calculate(filtered, revise, adjusted: false);
        var lAdj = LaborRateService.Calculate(filtered, revise, adjusted: true);
        var formworkBA = new BeforeAfter(
            StatisticsService.Summarize(Finite(lRaw.Select(x => x.FormworkPerM2)).Select(v => v * f)),
            StatisticsService.Summarize(Finite(lAdj.Select(x => x.FormworkPerM2)).Select(v => v * f)));
        var rebarBA = new BeforeAfter(
            StatisticsService.Summarize(Finite(lRaw.Select(x => x.RebarPerTon)).Select(v => v * f)),
            StatisticsService.Summarize(Finite(lAdj.Select(x => x.RebarPerTon)).Select(v => v * f)));

        var result = new AnalysisResult
        {
            Criteria = c,
            ProjectCount = filtered.Count,
            SelectionLabel = SelectionLabel(c),
            ConditionParts = ConditionParts(c),
            Assess = assessBA,
            Revise = reviseBA,
            TempUnit = tempUnitBA,
            TempRatio = tempRatioBA,
            Formwork = formworkBA,
            Rebar = rebarBA,
            AssessDeflatorMean = DeflatorService.MeanDeflator(assess, c.Trade, c.Work, c.Item),
            ReviseDeflatorMean = DeflatorService.MeanDeflator(revise, c.Trade, c.Work, c.Item),
            TempN = Math.Max(tempUnitBA.Before.N, tempUnitBA.After.N),
            FormworkN = Math.Max(formworkBA.Before.N, formworkBA.After.N),
            RebarN = Math.Max(rebarBA.Before.N, rebarBA.After.N),
        };

        // ── グラフ ──
        var areaLabel = c.BasisMode.ToLabel() + "(㎡)";
        result.Charts.Add(new ChartSeries("assess", "査定単価", "円/㎡", areaLabel,
            AreaPoints(aRaw, f), AreaPoints(aAdj, f), assessBA));
        result.Charts.Add(new ChartSeries("revise", "修正単価", "円/㎡", areaLabel,
            AreaPoints(rRaw, f), AreaPoints(rAdj, f), reviseBA));
        result.Charts.Add(new ChartSeries("temp", "仮設工事単価", "円/㎡", "物件インデックス",
            IndexPoints(tRaw.Select(x => (x.ProjectId, (double?)(x.UnitCostPerM2 * f)))),
            IndexPoints(tAdj.Select(x => (x.ProjectId, (double?)(x.UnitCostPerM2 * f)))), tempUnitBA));
        result.Charts.Add(new ChartSeries("formwork", "型枠労務歩掛", "円/㎡", "物件インデックス",
            IndexPoints(lRaw.Select(x => (x.ProjectId, x.FormworkPerM2 * f))),
            IndexPoints(lAdj.Select(x => (x.ProjectId, x.FormworkPerM2 * f))), formworkBA));
        result.Charts.Add(new ChartSeries("rebar", "鉄筋労務歩掛", "円/t", "物件インデックス",
            IndexPoints(lRaw.Select(x => (x.ProjectId, x.RebarPerTon * f))),
            IndexPoints(lAdj.Select(x => (x.ProjectId, x.RebarPerTon * f))), rebarBA));

        // ── 物件一覧（面積レンジも反映済みの filtered を使う） ──
        var reviseByPid = new Dictionary<string, double>();
        foreach (var x in rAdj) reviseByPid.TryAdd(x.ProjectId, x.YenPerM2 * f);
        result.Projects = filtered.Select(p => new ProjectRow(
            p.ProjectId, p.Name, p.Prefecture, p.City, p.Town, p.Structure, p.Form,
            p.FloorsAbove, p.FloorsBelow, p.LegalFloorArea, p.ConstructionFloorArea,
            reviseByPid.TryGetValue(p.ProjectId, out var y) ? y : (double?)null)).ToList();

        // ── 左パネルの選択肢 ──
        result.Facets = ProjectFilterService.BuildFacets(projects, c);
        var bounds = ProjectFilterService.AreaBounds(projects, c.BasisMode);
        result.AreaBoundMin = bounds?.Min ?? 0;
        result.AreaBoundMax = bounds?.Max ?? 0;
        var allCosts = assess.Concat(revise).ToList();
        result.TradeOptions = TradeOptions(allCosts);
        result.WorkOptions = WorkOptions(allCosts, c.Trade);
        result.ItemOptions = ItemOptions(allCosts, c.Trade, c.Work);

        result.StatusLine =
            $"表示更新: Projects絞込={filtered.Count:N0}件 / 査定n={assessBA.After.N} / 修正n={reviseBA.After.N}" +
            $" / 仮設工事n={result.TempN} / 型枠n={result.FormworkN} / 鉄筋n={result.RebarN}";
        return result;
    }

    // ── 工種ドリルダウン（Python の init_filters / on_drilldown_change） ──

    public static List<string> TradeOptions(IEnumerable<CostRecord> costs) =>
        WithUnspecified(costs.Select(x => x.Trade));

    public static List<string> WorkOptions(IEnumerable<CostRecord> costs, string trade) =>
        FilterCriteria.IsSet(trade)
            ? WithUnspecified(costs.Where(x => x.Trade == trade).Select(x => x.Work))
            : new List<string> { FilterCriteria.Unspecified };

    public static List<string> ItemOptions(IEnumerable<CostRecord> costs, string trade, string work) =>
        FilterCriteria.IsSet(trade) && FilterCriteria.IsSet(work)
            ? WithUnspecified(costs.Where(x => x.Trade == trade && x.Work == work).Select(x => x.Item))
            : new List<string> { FilterCriteria.Unspecified };

    /// <summary>条件の箇条（Python の _current_condition_lines）。</summary>
    public static List<string> ConditionParts(FilterCriteria c)
    {
        var parts = new List<string>();
        if (FilterCriteria.IsSet(c.Prefecture)) parts.Add(c.Prefecture);
        if (FilterCriteria.IsSet(c.City)) parts.Add(c.City);
        if (FilterCriteria.IsSet(c.Town)) parts.Add(c.Town);
        if (FilterCriteria.IsSet(c.Structure)) parts.Add(c.Structure);
        if (FilterCriteria.IsSet(c.Form)) parts.Add(c.Form);
        if (FilterCriteria.IsSet(c.FloorsAbove)) parts.Add($"地上{c.FloorsAbove}階");
        if (FilterCriteria.IsSet(c.FloorsBelow)) parts.Add($"地下{c.FloorsBelow}階");
        if (FilterCriteria.IsSet(c.Trade)) parts.Add($"工種:{c.Trade}");
        if (FilterCriteria.IsSet(c.Work)) parts.Add($"工事項目:{c.Work}");
        if (FilterCriteria.IsSet(c.Item)) parts.Add($"項目:{c.Item}");
        parts.Add($"基準面積:{c.BasisMode.ToLabel()}");
        parts.Add($"表示:{c.CostMode.ToLabel()}");
        var mn = (long)(c.AreaMin ?? 0);
        var mx = (long)(c.AreaMax ?? 0);
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"面積レンジ:{mn:N0}〜{mx:N0}㎡"));
        return parts;
    }

    public static string SelectionLabel(FilterCriteria c)
    {
        var label = FilterCriteria.IsSet(c.Trade) ? $"工種:{c.Trade}" : "工種:未指定";
        if (FilterCriteria.IsSet(c.Work)) label += $" / 工事項目:{c.Work}";
        if (FilterCriteria.IsSet(c.Item)) label += $" / 項目:{c.Item}";
        return label;
    }

    // ------------------------------------------------------------------

    private static List<string> WithUnspecified(IEnumerable<string> values)
    {
        var list = values.Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        list.Insert(0, FilterCriteria.Unspecified);
        return list;
    }

    private static IEnumerable<double> Finite(IEnumerable<double?> values) =>
        values.Where(v => v is double d && double.IsFinite(d)).Select(v => v!.Value);

    private static List<ChartPoint> AreaPoints(IEnumerable<UnitPricePoint> points, double f) =>
        points.Where(p => double.IsFinite(p.YenPerM2))
              .Select(p => new ChartPoint(p.ProjectId, p.Area, p.YenPerM2 * f))
              .ToList();

    /// <summary>X に元の並び順のインデックス（Python の np.where(mask)[0]）を使う。欠損は除外。</summary>
    private static List<ChartPoint> IndexPoints(IEnumerable<(string ProjectId, double? Value)> values)
    {
        var list = new List<ChartPoint>();
        var i = 0;
        foreach (var (pid, v) in values)
        {
            if (v is double d && double.IsFinite(d)) list.Add(new ChartPoint(pid, i, d));
            i++;
        }
        return list;
    }
}
