using System.Globalization;
using System.Text.RegularExpressions;
using Ats.Domain.Enums;

namespace Ats.Application.Branding;

// The accent colour is emitted into a style element, so it is validated on the way in and again on
// the way out. It is the only tenant-supplied value in the product that reaches CSS.
public static partial class BrandColor
{
    public const string DefaultAccent = "#0085CA";       // NowOnline Sky Blue
    public const string DefaultAccentHover = "#128FCF";  // the design system's own hover token

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();

    // Returns the upper-cased colour, or null when the input is not a plain 6-digit hex colour.
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return HexPattern().IsMatch(trimmed) ? trimmed.ToUpperInvariant() : null;
    }

    // Mixes the colour toward white by amount (0..1). Null when the input is invalid.
    public static string? Lighten(string? value, double amount)
    {
        var hex = Normalize(value);
        if (hex is null) return null;
        var t = Math.Clamp(amount, 0d, 1d);

        var r = Channel(hex, 1, t);
        var g = Channel(hex, 3, t);
        var b = Channel(hex, 5, t);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static int Channel(string hex, int offset, double t)
    {
        var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Math.Clamp((int)Math.Round(c + (255 - c) * t), 0, 255);
    }
}

public sealed record TenantBranding(
    string TenantName,
    string TenantSlug,
    string Accent,
    string AccentHover,
    SidebarTheme SidebarTheme,
    string? CareerHeroHeadline,
    string? CareerHeroHeadlineOutlined,
    string? CareerHeroIntro)
{
    public bool IsDarkSidebar => SidebarTheme == SidebarTheme.Dark;
}
