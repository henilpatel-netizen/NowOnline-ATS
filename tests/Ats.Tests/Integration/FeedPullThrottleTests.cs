using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Integration;

public class FeedPullThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Records_when_never_pulled_before()
    {
        Assert.True(FeedPullThrottle.ShouldRecord(null, Now));
    }

    [Fact]
    public void Records_when_last_pull_is_over_a_minute_ago()
    {
        Assert.True(FeedPullThrottle.ShouldRecord(Now.AddSeconds(-61), Now));
    }

    [Fact]
    public void Skips_when_last_pull_is_within_the_minute()
    {
        Assert.False(FeedPullThrottle.ShouldRecord(Now.AddSeconds(-59), Now));
    }

    [Fact]
    public void Skips_a_duplicate_at_the_same_instant()
    {
        Assert.False(FeedPullThrottle.ShouldRecord(Now, Now));
    }

    [Fact]
    public void Records_when_the_clock_appears_to_go_backwards()
    {
        // A stored timestamp in the future (clock skew) must not wedge the throttle shut forever.
        Assert.True(FeedPullThrottle.ShouldRecord(Now.AddSeconds(120), Now));
    }
}
