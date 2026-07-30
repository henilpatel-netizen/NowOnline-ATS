namespace Ats.Application.Common;

public static class DashboardMath
{
    // Mean of the spans in whole days, negatives clamped to zero. Null for an empty set.
    public static int? MeanDays(IReadOnlyCollection<TimeSpan> spans)
    {
        if (spans.Count == 0) return null;
        var avg = spans.Average(s => Math.Max(0, s.TotalDays));
        return (int)Math.Round(avg, MidpointRounding.AwayFromZero);
    }

    // Percentage numerator/denominator, rounded. Null when the denominator is zero.
    public static int? Percent(int numerator, int denominator)
    {
        if (denominator == 0) return null;
        return (int)Math.Round(numerator * 100.0 / denominator, MidpointRounding.AwayFromZero);
    }

    // Whole-percent split of counts that always totals 100 (0 when everything is zero).
    // Rounding drift is handed to the buckets with the largest fractional parts so bars never
    // over- or under-shoot 100.
    public static int[] Split(IReadOnlyList<int> counts)
    {
        var total = counts.Sum();
        if (total == 0) return counts.Select(_ => 0).ToArray();

        var raw = counts.Select(c => c * 100.0 / total).ToArray();
        var floored = raw.Select(r => (int)Math.Floor(r)).ToArray();
        var remainder = 100 - floored.Sum();

        var order = Enumerable.Range(0, counts.Count)
            .OrderByDescending(i => raw[i] - floored[i])
            .ThenBy(i => i)
            .ToArray();
        for (var k = 0; k < remainder; k++) floored[order[k % order.Length]]++;
        return floored;
    }
}
