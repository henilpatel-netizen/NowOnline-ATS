namespace Ats.Application.Common;

// `now` is a parameter rather than DateTimeOffset.UtcNow so this stays pure and testable.
public static class RelativeTime
{
    public static int WholeDays(DateTimeOffset at, DateTimeOffset now)
    {
        var span = now - at;
        return span < TimeSpan.Zero ? 0 : (int)span.TotalDays;
    }

    public static string Long(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is null) return "never";

        var span = now - at.Value;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return Plural((int)span.TotalMinutes, "minute");
        if (span < TimeSpan.FromDays(1)) return Plural((int)span.TotalHours, "hour");
        return Plural((int)span.TotalDays, "day");
    }

    public static string ShortAge(DateTimeOffset at, DateTimeOffset now)
    {
        var days = WholeDays(at, now);
        return days == 0 ? "today" : $"{days}d";
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
}
