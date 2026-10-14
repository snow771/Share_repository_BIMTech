namespace SimilaritySearchApp.Tests;

public class CostAggregationTests
{
    [Fact]
    public void UnitPrice_sums_per_project_and_drops_zero_area()
    {
        var p = new List<ProjectRecord>
        {
            new() { ProjectId = "J1", LegalFloorArea = 100 },
            new() { ProjectId = "J2", LegalFloorArea = 0 },
        };
        var c = new[]
        {
            new CostRecord { ProjectId = "J1", Trade = "内装", Amount = 1000, AmountAdj = 1100 },
            new CostRecord { ProjectId = "J1", Trade = "内装", Amount = 500, AmountAdj = 500 },
            new CostRecord { ProjectId = "J2", Trade = "内装", Amount = 999, AmountAdj = 999 },
        };
        var u = FilterCriteria.Unspecified;

        var raw = UnitPriceService.Build(p, c, BasisMode.LegalFloorArea, "内装", u, u, adjusted: false);
        var adj = UnitPriceService.Build(p, c, BasisMode.LegalFloorArea, "内装", u, u, adjusted: true);

        Assert.Single(raw);
        Assert.Equal(15, raw[0].YenPerM2, 6);
        Assert.Equal(16, adj[0].YenPerM2, 6);
    }

    [Fact]
    public void TemporaryWork_ratio()
    {
        var p = new List<ProjectRecord> { new() { ProjectId = "J1", ConstructionFloorArea = 10 } };
        var c = new[]
        {
            new CostRecord { ProjectId = "J1", WorkTypeCode = "01-01", Amount = 100 },
            new CostRecord { ProjectId = "J1", WorkTypeCode = "02-01", Amount = 300 },
            new CostRecord { ProjectId = "J1", WorkTypeCode = "06-01", Amount = 999 },
        };
        var r = TemporaryWorkService.Calculate(p, c, adjusted: false).Single();
        Assert.Equal(10, r.UnitCostPerM2, 6);
        Assert.Equal(25, r.RatioPercent, 6);
    }

    [Fact]
    public void Rebar_divides_by_raw_value_without_kg_to_ton()
    {
        var p = new List<ProjectRecord> { new() { ProjectId = "J1", ConstructionFloorArea = 10, RebarKgPerM2 = 4, Formwork = 0 } };
        var c = new[] { new CostRecord { ProjectId = "J1", WorkTypeCode = "02-05-30000", Amount = 400 } };
        var r = LaborRateService.Calculate(p, c, adjusted: false).Single();
        Assert.Equal(100, r.RebarPerTon);
        Assert.Null(r.FormworkPerM2);
    }

    [Fact]
    public void Formwork_divides_by_formwork_area()
    {
        var p = new List<ProjectRecord> { new() { ProjectId = "J1", Formwork = 50 } };
        var c = new[] { new CostRecord { ProjectId = "J1", WorkTypeCode = "02-04-10000", Amount = 500, AmountAdj = 600 } };
        var r = LaborRateService.Calculate(p, c, adjusted: true).Single();
        Assert.Equal(12, r.FormworkPerM2);
        Assert.Null(r.RebarPerTon);
    }
}
