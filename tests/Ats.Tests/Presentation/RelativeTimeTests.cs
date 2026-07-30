using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Presentation;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(-30, "just now")]
    [InlineData(-60, "1 minute ago")]
    [InlineData(-240, "4 minutes ago")]
    [InlineData(-3600, "1 hour ago")]
    [InlineData(-7200, "2 hours ago")]
    [InlineData(-86400, "1 day ago")]
    [InlineData(-259200, "3 days ago")]
    public void Long_form_describes_the_age(int offsetSeconds, string expected)
    {
        Assert.Equal(expected, RelativeTime.Long(Now.AddSeconds(offsetSeconds), Now));
    }

    [Fact]
    public void Long_form_handles_a_null_timestamp()
    {
        Assert.Equal("never", RelativeTime.Long(null, Now));
    }

    [Fact]
    public void Long_form_treats_a_future_timestamp_as_now()
    {
        Assert.Equal("just now", RelativeTime.Long(Now.AddMinutes(5), Now));
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(-86400, "1d")]
    [InlineData(-259200, "3d")]
    [InlineData(-950400, "11d")]
    public void Short_form_is_a_compact_day_count(int offsetSeconds, string expected)
    {
        Assert.Equal(expected, RelativeTime.ShortAge(Now.AddSeconds(offsetSeconds), Now));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-86400, 1)]
    [InlineData(-950400, 11)]
    public void Whole_days_counts_elapsed_days(int offsetSeconds, int expected)
    {
        Assert.Equal(expected, RelativeTime.WholeDays(Now.AddSeconds(offsetSeconds), Now));
    }
}
