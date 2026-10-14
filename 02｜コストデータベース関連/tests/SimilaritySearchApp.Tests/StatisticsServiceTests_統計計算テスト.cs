namespace SimilaritySearchApp.Tests;

public class StatisticsServiceTests
{
    [Fact]
    public void Percentile_matches_numpy_linear()
    {
        var s = new List<double> { 1, 2, 3, 4 };
        Assert.Equal(1.75, StatisticsService.Percentile(s, 25), 10);
        Assert.Equal(2.5, StatisticsService.Percentile(s, 50), 10);
        Assert.Equal(3.25, StatisticsService.Percentile(s, 75), 10);
    }

    [Fact]
    public void Summarize_ignores_nan_and_empty_is_null()
    {
        var r = StatisticsService.Summarize(new[] { double.NaN, 10, 20, 30, double.PositiveInfinity });
        Assert.Equal(3, r.N);
        Assert.Equal(20, r.Median);
        Assert.Equal(15, r.Q1);
        Assert.Equal(25, r.Q3);

        var e = StatisticsService.Summarize(Array.Empty<double>());
        Assert.Equal(0, e.N);
        Assert.Null(e.Median);
    }

    [Fact]
    public void Scale_multiplies_values_and_keeps_n()
    {
        var r = StatisticsService.Summarize(new double[] { 10, 20 }).Scale(2);
        Assert.Equal(2, r.N);
        Assert.Equal(30, r.Median);
    }
}
