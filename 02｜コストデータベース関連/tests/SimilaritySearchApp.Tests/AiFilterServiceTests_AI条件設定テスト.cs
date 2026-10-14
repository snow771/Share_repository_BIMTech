using System.Text.Json.Nodes;

namespace SimilaritySearchApp.Tests;

public class AiFilterServiceTests
{
    [Fact]
    public void PickOption_exact_norm_partial()
    {
        var o = new List<string> { "(未指定)", "東京都", "ｺﾝｸﾘｰﾄ工事" };
        Assert.Equal("東京都", AiFilterService.PickOption(o, "東京都"));
        Assert.Equal("東京都", AiFilterService.PickOption(o, "東京"));
        Assert.Equal("(未指定)", AiFilterService.PickOption(o, "大阪"));
        Assert.Equal("(未指定)", AiFilterService.PickOption(o, null));
    }

    [Theory]
    [InlineData("確認観点を教えて", ChatIntent.ReviewPoints)]
    [InlineData("この物件は？", ChatIntent.SelectedProject)]
    [InlineData("要約して", ChatIntent.Summary)]
    [InlineData("東京都の内装の単価は", ChatIntent.Filter)]
    [InlineData("RC造の相場感", ChatIntent.Filter)]
    public void ClassifyChat(string text, ChatIntent expected) =>
        Assert.Equal(expected, InsightTextService.ClassifyChat(text, dataLoaded: true));

    [Fact]
    public void ApplyHints_maps_floors_scale_and_trade()
    {
        var u = FilterCriteria.Unspecified;
        var projects = Enumerable.Range(1, 9)
            .Select(i => new ProjectRecord { ProjectId = $"J{i}", LegalFloorArea = i * 100 })
            .ToList();
        var opts = new FilterOptionsSnapshot(
            new() { u, "東京都" }, new() { u }, new() { u }, new() { u, "RC造" }, new() { u },
            new() { u, "3", "7" }, new() { u, "1" }, new() { u, "内装工事" }, new() { u }, new() { u },
            100, 900);
        var hierarchy = new List<TradePath> { new("内装工事", "内装工事", "ﾎﾞｰﾄﾞ") };
        var ai = new JsonObject
        {
            ["pref_hint"] = "東京", ["structure_hint"] = "RC", ["floors_up_hint"] = "(未指定)",
            ["scale_hint"] = "小規模", ["trade_hint"] = "内装", ["reason"] = "テスト",
        };

        var r = AiFilterService.ApplyHints(ai, "東京都RC造7階 地下1階の内装", new FilterCriteria(), opts, projects, hierarchy,
            _ => new List<string> { u, "内装工事" }, (_, _) => new List<string> { u, "ﾎﾞｰﾄﾞ" });

        Assert.Equal("東京都", r.Criteria.Prefecture);
        Assert.Equal("RC造", r.Criteria.Structure);
        Assert.Equal("7", r.Criteria.FloorsAbove);
        Assert.Equal("1", r.Criteria.FloorsBelow);
        Assert.Equal(100, r.Criteria.AreaMin);
        Assert.Equal(366.666, r.Criteria.AreaMax!.Value, 2);
        Assert.Equal("内装工事", r.Criteria.Trade);
        Assert.Equal("内装工事", r.Criteria.Work);
        Assert.Equal("テスト", r.Reason);
    }
}
