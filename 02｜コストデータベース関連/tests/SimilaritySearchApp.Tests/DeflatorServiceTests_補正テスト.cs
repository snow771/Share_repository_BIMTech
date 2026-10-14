namespace SimilaritySearchApp.Tests;

public class DeflatorServiceTests
{
    [Fact]
    public void Apply_chains_masters_and_defaults_to_one()
    {
        var costs = new List<CostRecord>
        {
            new() { ProjectId = "J1", WorkTypeCode = "01-01", Term = "T1", Amount = 100 },
            new() { ProjectId = "J1", WorkTypeCode = "99-99", Term = "T1", Amount = 50 },
            new() { ProjectId = "J1", WorkTypeCode = "01-01", Term = "T2", Amount = 10 },
        };
        var cat = new[] { new CategoryRow("01-01", 1), new CategoryRow("01-01", 2) };
        var ia = new[] { new IndexAllocationRow(1, "A") };
        var de = new[] { new DeflatorRow("T1", "A", 1.2), new DeflatorRow("T2", "A", 0) };

        DeflatorService.Apply(costs, cat, ia, de);

        Assert.Equal(120, costs[0].AmountAdj, 6);
        Assert.Equal(1.0, costs[1].Deflator);
        Assert.Equal(50, costs[1].AmountAdj);
        Assert.Equal(1.0, costs[2].Deflator);
    }

    [Fact]
    public void AssignTerm_inclusive_first_match()
    {
        var t = new[]
        {
            new TermRow("T1", new DateTime(2020, 1, 1), new DateTime(2020, 12, 31)),
            new TermRow("T2", new DateTime(2020, 6, 1), new DateTime(2021, 12, 31)),
        };
        Assert.Equal("T1", DeflatorService.AssignTerm(new DateTime(2020, 12, 31), t));
        Assert.Equal("T2", DeflatorService.AssignTerm(new DateTime(2021, 1, 1), t));
        Assert.Equal("", DeflatorService.AssignTerm(null, t));
    }

    [Fact]
    public void MeanDeflator_filters_by_trade()
    {
        var costs = new[]
        {
            new CostRecord { Trade = "内装工事", Deflator = 1.1 },
            new CostRecord { Trade = "内装工事", Deflator = 1.3 },
            new CostRecord { Trade = "仮設工事", Deflator = 2.0 },
        };
        Assert.Equal(1.2, DeflatorService.MeanDeflator(costs, "内装工事", FilterCriteria.Unspecified, FilterCriteria.Unspecified)!.Value, 6);
        Assert.Null(DeflatorService.MeanDeflator(costs, "存在しない", FilterCriteria.Unspecified, FilterCriteria.Unspecified));
    }
}
