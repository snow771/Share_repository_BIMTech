namespace SimilaritySearchApp.Tests;

public class AnalysisServiceTests
{
    private static readonly List<ProjectRecord> Projects = new()
    {
        new() { ProjectId = "J1", LegalFloorArea = 10, ConstructionFloorArea = 10 },
    };

    private static readonly List<CostRecord> Revise = new()
    {
        new() { ProjectId = "J1", WorkTypeCode = "01-01", Trade = "仮設", Amount = 705, AmountAdj = 705 },
    };

    [Fact]
    public void Genka_mode_scales_unit_prices_but_not_ratio()
    {
        var net = AnalysisService.Analyze(Projects, new List<CostRecord>(), Revise, new FilterCriteria());
        var gen = AnalysisService.Analyze(Projects, new List<CostRecord>(), Revise,
            new FilterCriteria { CostMode = CostMode.ConstructionCost });

        Assert.Equal(70.5, net.Revise.After.Median!.Value, 6);
        Assert.Equal(100, gen.Revise.After.Median!.Value, 6);
        Assert.Equal(net.TempRatio.After.Median, gen.TempRatio.After.Median);
        Assert.Equal(100, gen.TempUnit.After.Median!.Value, 6);
    }

    [Fact]
    public void Project_rows_carry_revise_unit_price_and_charts_have_five_series()
    {
        var r = AnalysisService.Analyze(Projects, new List<CostRecord>(), Revise, new FilterCriteria());
        Assert.Equal(70.5, r.Projects.Single().ReviseYenPerM2!.Value, 6);
        Assert.Equal(new[] { "assess", "revise", "temp", "formwork", "rebar" }, r.Charts.Select(x => x.Key));
        Assert.Equal(new[] { FilterCriteria.Unspecified, "仮設" }, r.TradeOptions);
    }

    [Fact]
    public void ConditionParts_lists_set_values()
    {
        var parts = AnalysisService.ConditionParts(new FilterCriteria { Prefecture = "東京都", FloorsAbove = "7", AreaMin = 0, AreaMax = 1200 });
        Assert.Contains("東京都", parts);
        Assert.Contains("地上7階", parts);
        Assert.Contains("面積レンジ:0〜1,200㎡", parts);
    }
}
