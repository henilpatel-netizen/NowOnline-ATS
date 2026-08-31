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

    // ---- Contrast (A11Y-3) --------------------------------------------------------------------
    // Buttons and chips used to force white text on the tenant's accent. A pale accent (yellow,
    // lime) then produced white-on-pale, which no one can read, and the focus ring vanished. The
    // readable text colour is therefore derived from the accent rather than assumed.

    // Text placed on the accent. Dark ink where the accent is light, white where it is dark.
    public const string OnAccentLight = "#FFFFFF";
    public const string OnAccentDark = "#0C2340";   // NowOnline Oxford Blue

    // WCAG 2.1 relative luminance. Null for an invalid colour.
    public static double? RelativeLuminance(string? value)
    {
        var hex = Normalize(value);
        if (hex is null) return null;
        return 0.2126 * Linear(hex, 1) + 0.7152 * Linear(hex, 3) + 0.0722 * Linear(hex, 5);
    }

    // WCAG 2.1 contrast ratio, 1.0 (identical) to 21.0 (black on white). Null if either is invalid.
    public static double? ContrastRatio(string? a, string? b)
    {
        if (RelativeLuminance(a) is not double la || RelativeLuminance(b) is not double lb) return null;
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    // The readable text colour for this accent: whichever of dark ink / white contrasts better.
    // Falls back to the default accent's pairing when the input is invalid.
    public static string OnAccent(string? accent)
    {
        var hex = Normalize(accent) ?? DefaultAccent;
        var onDark = ContrastRatio(hex, OnAccentDark) ?? 0;
        var onLight = ContrastRatio(hex, OnAccentLight) ?? 0;
        return onDark >= onLight ? OnAccentDark : OnAccentLight;
    }

    // Best achievable text contrast on this accent, for warning in the branding editor. WCAG AA
    // requires 4.5:1 for normal text and 3:1 for large text and UI component boundaries.
    public static double BestTextContrast(string? accent)
    {
        var hex = Normalize(accent) ?? DefaultAccent;
        return Math.Max(ContrastRatio(hex, OnAccentDark) ?? 0, ContrastRatio(hex, OnAccentLight) ?? 0);
    }

    // True when the accent can carry normal-size text at AA (4.5:1) with its paired text colour.
    public static bool MeetsAaText(string? accent) => BestTextContrast(accent) >= 4.5;

    // The app's lightest surface. The focus ring is drawn against this, so a pale accent ring
    // would be invisible on it.
    private const string SurfaceLight = "#FFFFFF";

    // Focus-ring colour. WCAG 2.1 (1.4.11) wants a non-text indicator at 3:1 against what is
    // behind it, so a too-light accent falls back to dark ink and the ring stays visible.
    public static string FocusRing(string? accent)
    {
        var hex = Normalize(accent) ?? DefaultAccent;
        return (ContrastRatio(hex, SurfaceLight) ?? 0) >= 3.0 ? hex : OnAccentDark;
    }

    // The subtle surface links are most often drawn on (cards, table heads). Using the lightest
    // surface would let a colour pass here and still fail on a card.
    private const string SurfaceSubtle = "#F5F6F7";

    // Link/`.btn-link` text colour. The raw accent is text on a light surface, so a pale accent
    // fails AA — the default #0085CA is only 4.03:1. Darkening preserves the tenant's hue where a
    // flat dark fallback would turn every link navy; the fallback is only reached by an accent so
    // pale that no darkening helps.
    public static string AccentText(string? accent)
    {
        var hex = Normalize(accent) ?? DefaultAccent;
        for (var step = 0; step <= 20; step++)
        {
            var candidate = step == 0 ? hex : Darken(hex, step * 0.05);
            if ((ContrastRatio(candidate, SurfaceSubtle) ?? 0) >= 4.5) return candidate;
        }
        return OnAccentDark;
    }

    private static string Darken(string hex, double amount)
    {
        var t = Math.Clamp(amount, 0d, 1d);
        var r = DarkChannel(hex, 1, t);
        var g = DarkChannel(hex, 3, t);
        var b = DarkChannel(hex, 5, t);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static int DarkChannel(string hex, int offset, double t)
    {
        var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Math.Clamp((int)Math.Round(c * (1 - t)), 0, 255);
    }

    private static double Linear(string hex, int offset)
    {
        var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
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
