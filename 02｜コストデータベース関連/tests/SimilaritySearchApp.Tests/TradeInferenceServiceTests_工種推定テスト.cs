namespace SimilaritySearchApp.Tests;

public class TradeInferenceServiceTests
{
    [Fact]
    public void Master_has_25_trades() => Assert.Equal(25, TradeMaster.Entries.Count);

    [Fact]
    public void Infer_interior_from_item_alias()
    {
        var h = TradeInferenceService.InferFromRequest("RC造7階のﾌﾛｰﾘﾝｸﾞ単価");
        Assert.Equal("内装工事", h.Trade);
        Assert.Equal("内装工事", h.Work);
    }

    [Fact]
    public void Resolve_picks_best_hierarchy_path()
    {
        var recs = new List<TradePath>
        {
            new("内装工事", "内装工事", "ﾎﾞｰﾄﾞ"),
            new("仮設工事", "足場工事", "足場工事"),
        };
        var u = FilterCriteria.Unspecified;
        var r = TradeInferenceService.Resolve(new TradeHint(u, u, u), "足場の単価", recs,
            new[] { u, "仮設工事", "内装工事" });
        Assert.Equal("仮設工事", r.Trade);
        Assert.Equal("足場工事", r.Work);
    }

    [Fact]
    public void Resolve_returns_unspecified_when_nothing_matches()
    {
        var recs = new List<TradePath> { new("内装工事", "内装工事", "ﾎﾞｰﾄﾞ") };
        var u = FilterCriteria.Unspecified;
        var r = TradeInferenceService.Resolve(new TradeHint(u, u, u), "東京都の単価", recs, new[] { u, "内装工事" });
        Assert.Equal(u, r.Trade);
    }

    [Fact]
    public void BuildHierarchy_dedups_in_order()
    {
        var costs = new[]
        {
            new CostRecord { Trade = "A", Work = "B", Item = "C" },
            new CostRecord { Trade = "A", Work = "B", Item = "C" },
            new CostRecord { Trade = "X", Work = "Y", Item = "Z" },
        };
        Assert.Equal(2, TradeInferenceService.BuildHierarchy(costs).Count);
    }
}
