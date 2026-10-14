namespace SimilaritySearchApp.Tests;

public class StandardPriceServiceTests
{
    private static readonly List<ProjectRecord> Projects = new()
    {
        new() { ProjectId = "J1", LegalFloorArea = 100 },
        new() { ProjectId = "J2", LegalFloorArea = 200 },
    };

    private static readonly List<CostRecord> Revise = new()
    {
        new() { ProjectId = "J1", WorkTypeCode = "06-01-10000", Trade = "内装工事", Work = "内装工事", Item = "ﾎﾞｰﾄﾞ", Amount = 1000, AmountAdj = 1000 },
        new() { ProjectId = "J2", WorkTypeCode = "06-01-10000", Trade = "内装工事", Work = "内装工事", Item = "ﾎﾞｰﾄﾞ", Amount = 4000, AmountAdj = 4000 },
        new() { ProjectId = "J2", WorkTypeCode = "06-01-20000", Trade = "内装工事", Work = "内装工事", Item = "置床", Amount = 500, AmountAdj = 500 },
        new() { ProjectId = "J1", WorkTypeCode = "01-01-10000", Trade = "仮設工事", Work = "足場工事", Item = "足場工事", Amount = 300, AmountAdj = 300 },
    };

    private static readonly List<MaterialRecord> Materials = new()
    {
        new() { WorkTypeCode = "06-01-10000", MaterialCode = "06-01-10000-1", Name = "PB 12.5", AssessPrice = 0, RevisePrice = null },
        new() { WorkTypeCode = "06-01-10000", MaterialCode = "06-01-10000-2", Name = "PB 9.5", AssessPrice = 800, RevisePrice = 780 },
        new() { WorkTypeCode = "06-01-20000", MaterialCode = "06-01-20000-1", Name = "置床", AssessPrice = 5000, RevisePrice = 4800 },
        new() { WorkTypeCode = "01-01-10000", MaterialCode = "01-01-10000-1", Name = "枠組足場", AssessPrice = 1200, RevisePrice = 1100 },
    };

    private static StandardPriceResult Run(FilterCriteria c, string? selectedId = null) =>
        StandardPriceService.Build(Materials, Projects, new List<CostRecord>(), Revise, c,
            Projects.FirstOrDefault(p => p.ProjectId == selectedId));

    [Fact]
    public void No_filter_shows_all_materials()
    {
        var r = Run(new FilterCriteria());
        Assert.Equal(4, r.MaterialCount);
        Assert.Equal(3, r.GroupCount);
    }

    [Fact]
    public void Trade_filter_narrows_to_codes_of_that_trade()
    {
        var r = Run(new FilterCriteria { Trade = "内装工事" });
        Assert.Equal(new[] { "06-01-10000", "06-01-20000" }, r.Groups.Select(g => g.WorkTypeCode));
        Assert.Equal("ﾎﾞｰﾄﾞ", r.Groups[0].Item);
    }

    [Fact]
    public void Selected_project_intersects_with_trade_filter_and_shows_project_amounts()
    {
        // J1 の工種コードは 06-01-10000 と 01-01-10000。内装で絞ると 06-01-10000 だけ。
        var r = Run(new FilterCriteria { Trade = "内装工事" }, "J1");
        var g = Assert.Single(r.Groups);
        Assert.Equal("06-01-10000", g.WorkTypeCode);
        Assert.Equal(2, g.Materials.Count);
        Assert.Equal(1000, g.ProjectReviseAmount);
        Assert.Equal(10, g.ProjectRevisePerM2);
        Assert.Null(g.ProjectAssessAmount);
    }

    [Fact]
    public void Median_per_m2_uses_filtered_projects()
    {
        var g = Run(new FilterCriteria { Trade = "内装工事" }).Groups.First(x => x.WorkTypeCode == "06-01-10000");
        // J1: 1000/100 = 10, J2: 4000/200 = 20 → 中央値 15
        Assert.Equal(15, g.MedianRevisePerM2);
        Assert.Equal(2, g.SampleCount);
    }

    [Fact]
    public void Zero_and_null_prices_are_kept_distinct()
    {
        var m = Run(new FilterCriteria()).Groups.First(x => x.WorkTypeCode == "06-01-10000").Materials[0];
        Assert.Equal(0, m.AssessPrice);
        Assert.Null(m.RevisePrice);
    }

    [Fact]
    public void Empty_master_is_reported_as_unavailable()
    {
        var r = StandardPriceService.Build(new List<MaterialRecord>(), Projects, new List<CostRecord>(), Revise, new FilterCriteria(), null);
        Assert.False(r.Available);
        Assert.NotNull(r.Message);
    }
}
