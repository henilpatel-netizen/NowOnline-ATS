using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Dashboard;

public class DashboardMathTests
{
    [Fact]
    public void MeanDays_averages_span_lengths()
    {
        var spans = new[] { TimeSpan.FromDays(10), TimeSpan.FromDays(20), TimeSpan.FromDays(30) };
        Assert.Equal(20, DashboardMath.MeanDays(spans));
    }

    [Fact]
    public void MeanDays_rounds_to_nearest_whole_day()
    {
        var spans = new[] { TimeSpan.FromDays(10), TimeSpan.FromDays(11) }; // 10.5 -> 11
        Assert.Equal(11, DashboardMath.MeanDays(spans));
    }

    [Fact]
    public void MeanDays_is_null_for_an_empty_set()
    {
        Assert.Null(DashboardMath.MeanDays(Array.Empty<TimeSpan>()));
    }

    [Fact]
    public void MeanDays_treats_negative_spans_as_zero()
    {
        // A clock-skewed event landing before AppliedAt must not drag the mean negative.
        var spans = new[] { TimeSpan.FromDays(-5), TimeSpan.FromDays(10) };
        Assert.Equal(5, DashboardMath.MeanDays(spans));
    }

    [Theory]
    [InlineData(7, 9, 78)]     // 0.777... -> 78%
    [InlineData(1, 1, 100)]
    [InlineData(0, 5, 0)]
    public void Percent_rounds_a_ratio(int numerator, int denominator, int expected)
    {
        Assert.Equal(expected, DashboardMath.Percent(numerator, denominator));
    }

    [Fact]
    public void Percent_is_null_when_the_denominator_is_zero()
    {
        Assert.Null(DashboardMath.Percent(3, 0));
    }

    [Fact]
    public void Split_returns_percentages_that_sum_to_100()
    {
        var split = DashboardMath.Split(new[] { 61, 27, 12 });
        Assert.Equal(100, split.Sum());
    }

    [Fact]
    public void Split_absorbs_rounding_drift_into_the_largest_bucket()
    {
        // 1/1/1 -> 34/33/33: totals 100, first equal-fraction bucket takes the remainder.
        var split = DashboardMath.Split(new[] { 1, 1, 1 });
        Assert.Equal(100, split.Sum());
        Assert.Equal(34, split.Max());
    }

    [Fact]
    public void Split_of_all_zero_is_all_zero()
    {
        Assert.All(DashboardMath.Split(new[] { 0, 0, 0 }), p => Assert.Equal(0, p));
    }
}
