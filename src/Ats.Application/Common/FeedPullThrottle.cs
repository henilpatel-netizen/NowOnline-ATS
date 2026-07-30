namespace Ats.Application.Common;

// The vacancy feed can be pulled many times a minute; we persist the timestamp at most once a
// minute to avoid a write on every request. A future stored value (clock skew) counts as due.
public static class FeedPullThrottle
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);

    public static bool ShouldRecord(DateTimeOffset? lastPulledAt, DateTimeOffset now)
    {
        if (lastPulledAt is null) return true;
        var elapsed = now - lastPulledAt.Value;
        return elapsed >= MinInterval || elapsed < TimeSpan.Zero;
    }
}
