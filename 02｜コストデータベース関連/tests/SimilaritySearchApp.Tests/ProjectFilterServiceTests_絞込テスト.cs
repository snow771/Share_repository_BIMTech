namespace SimilaritySearchApp.Tests;

public class ProjectFilterServiceTests
{
    private static ProjectRecord P(string id, string pref, string city, double area, double? floors = null) =>
        new() { ProjectId = id, Prefecture = pref, City = city, LegalFloorArea = area, FloorsAbove = floors };

    [Fact]
    public void Apply_filters_attributes_and_area()
    {
        var all = new[] { P("J1", "東京都", "港区", 500, 7), P("J2", "東京都", "新宿区", 1500, 7), P("J3", "千葉県", "千葉市", 800, 3) };
        var c = new FilterCriteria { Prefecture = "東京都", FloorsAbove = "7", AreaMin = 400, AreaMax = 1000 };
        Assert.Equal(new[] { "J1" }, ProjectFilterService.Apply(all, c).Select(x => x.ProjectId));
    }

    [Fact]
    public void Apply_without_area_ignores_range()
    {
        var all = new[] { P("J1", "東京都", "港区", 500), P("J2", "東京都", "新宿区", 1500) };
        var c = new FilterCriteria { AreaMin = 400, AreaMax = 1000 };
        Assert.Equal(2, ProjectFilterService.Apply(all, c, includeArea: false).Count);
    }

    [Fact]
    public void Facets_count_excluding_own_dimension_and_cascade_city()
    {
        var all = new[] { P("J1", "東京都", "港区", 500), P("J2", "東京都", "新宿区", 1500), P("J3", "千葉県", "千葉市", 800) };
        var f = ProjectFilterService.BuildFacets(all, new FilterCriteria { Prefecture = "東京都" });
        Assert.Contains(f[ProjectFilterService.FacetPrefecture], o => o.Value == "千葉県" && o.Count == 1);
        Assert.DoesNotContain(f[ProjectFilterService.FacetCity], o => o.Value == "千葉市");
    }

    [Fact]
    public void Prefecture_facet_ignores_lower_city_selection()
    {
        var all = new[] { P("J1", "東京都", "港区", 500), P("J3", "千葉県", "千葉市", 800) };
        var f = ProjectFilterService.BuildFacets(all, new FilterCriteria { Prefecture = "東京都", City = "港区" });
        Assert.Contains(f[ProjectFilterService.FacetPrefecture], o => o.Value == "千葉県");
    }

    [Theory]
    [InlineData("10t", null)]
    [InlineData("PumpTruck", null)]
    [InlineData("nan", null)]
    [InlineData("共同住宅", "共同住宅")]
    public void CleanForm(string input, string? expected) =>
        Assert.Equal(expected, ProjectFilterService.CleanForm(input));

    [Fact]
    public void FormatFloor_drops_trailing_zero() =>
        Assert.Equal("7", ProjectFilterService.FormatFloor(7.0));
}
