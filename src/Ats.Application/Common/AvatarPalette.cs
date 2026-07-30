namespace Ats.Application.Common;

public sealed record AvatarColors(string Background, string Foreground);

// The five avatar colour pairs used throughout the redesign prototype. Colour is derived from the
// name so the same person is the same colour on every screen and every server.
public static class AvatarPalette
{
    public static readonly AvatarColors Neutral = new("#EFF0F2", "#5A6472");

    public static readonly IReadOnlyList<AvatarColors> Pairs = new[]
    {
        new AvatarColors("#EBF5FB", "#00679E"),   // sky
        new AvatarColors("#E8F6F0", "#00734D"),   // aqua
        new AvatarColors("#F0ECFB", "#5B3FBF"),   // violet
        new AvatarColors("#FDF3E7", "#A85400"),   // amber
        Neutral                                    // slate
    };

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return "?";

        if (tokens.Length == 1)
        {
            var single = tokens[0];
            return (single.Length >= 2 ? single[..2] : single).ToUpperInvariant();
        }

        return $"{tokens[0][0]}{tokens[1][0]}".ToUpperInvariant();
    }

    public static AvatarColors For(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Neutral;
        var index = (int)(Fnv1a(name.Trim().ToLowerInvariant()) % (uint)Pairs.Count);
        return Pairs[index];
    }

    // FNV-1a. Deterministic across processes, unlike string.GetHashCode(), which is randomized
    // per process in .NET and would give the same person a different colour on each server.
    private static uint Fnv1a(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }
}
