using Ats.Application.Branding;
using Xunit;

namespace Ats.Tests.Branding;

public class BrandColorTests
{
    [Theory]
    [InlineData("#0085CA")]
    [InlineData("#0085ca")]
    [InlineData("#FFFFFF")]
    [InlineData("#000000")]
    public void Normalize_accepts_six_digit_hex(string input)
    {
        Assert.Equal(input.ToUpperInvariant(), BrandColor.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0085CA")]                      // missing hash
    [InlineData("#0085C")]                      // five digits
    [InlineData("#0085CAA")]                    // seven digits
    [InlineData("#00 85CA")]                    // whitespace inside
    [InlineData("#GGGGGG")]                     // not hex
    [InlineData("red")]                         // named colour
    [InlineData("#0085CA;}")]                   // CSS escape attempt
    [InlineData("var(--x)")]
    [InlineData("#0085CA\";background:url(x)")] // attribute break-out attempt
    public void Normalize_rejects_anything_else(string? input)
    {
        Assert.Null(BrandColor.Normalize(input));
    }

    [Fact]
    public void Normalize_trims_surrounding_whitespace()
    {
        Assert.Equal("#0085CA", BrandColor.Normalize("  #0085CA  "));
    }

    [Fact]
    public void Lighten_moves_each_channel_toward_white()
    {
        // 0.08 toward white reproduces the design system's #0085CA -> #128FCF hover relationship
        // to within one step per channel: 0 -> 20, 133 -> 143, 202 -> 206.
        Assert.Equal("#148FCE", BrandColor.Lighten("#0085CA", 0.08));
    }

    [Fact]
    public void Lighten_clamps_at_white()
    {
        Assert.Equal("#FFFFFF", BrandColor.Lighten("#FFFFFF", 0.5));
    }

    [Fact]
    public void Lighten_returns_null_for_an_invalid_colour()
    {
        Assert.Null(BrandColor.Lighten("nonsense", 0.08));
    }

    // ---- Contrast (A11Y-3) --------------------------------------------------------------------

    [Fact]
    public void Luminance_matches_the_WCAG_reference_points()
    {
        Assert.Equal(0d, BrandColor.RelativeLuminance("#000000")!.Value, 4);
        Assert.Equal(1d, BrandColor.RelativeLuminance("#FFFFFF")!.Value, 4);
    }

    [Fact]
    public void Contrast_of_black_on_white_is_21_to_1()
    {
        Assert.Equal(21d, BrandColor.ContrastRatio("#000000", "#FFFFFF")!.Value, 2);
    }

    [Fact]
    public void Contrast_of_a_colour_with_itself_is_1_to_1()
    {
        Assert.Equal(1d, BrandColor.ContrastRatio("#0085CA", "#0085CA")!.Value, 4);
    }

    [Fact]
    public void A_pale_accent_gets_dark_text()
    {
        // The failure this exists to prevent: white text on pale yellow.
        Assert.Equal(BrandColor.OnAccentDark, BrandColor.OnAccent("#F5E663"));
    }

    [Fact]
    public void A_dark_accent_gets_white_text()
    {
        Assert.Equal(BrandColor.OnAccentLight, BrandColor.OnAccent("#0C2340"));
    }

    [Fact]
    public void The_default_accent_keeps_white_text()
    {
        // Guards the existing look: the NowOnline sky blue must not flip to dark ink.
        Assert.Equal(BrandColor.OnAccentLight, BrandColor.OnAccent(BrandColor.DefaultAccent));
    }

    [Fact]
    public void An_invalid_accent_falls_back_to_the_default_pairing()
    {
        Assert.Equal(BrandColor.OnAccent(BrandColor.DefaultAccent), BrandColor.OnAccent("not-a-colour"));
    }

    [Theory]
    [InlineData("#FFFFFF")]   // white accent
    [InlineData("#F5E663")]   // pale yellow
    [InlineData("#0C2340")]   // near-black
    public void The_paired_text_colour_always_reaches_AA(string accent)
    {
        var on = BrandColor.OnAccent(accent);
        Assert.True(BrandColor.ContrastRatio(accent, on) >= 4.5,
            $"{accent} on {on} = {BrandColor.ContrastRatio(accent, on)}");
    }

    [Fact]
    public void Mid_tone_accents_are_reported_as_failing_AA()
    {
        // Mid greys cannot reach 4.5:1 against either black or white; the editor should warn.
        Assert.False(BrandColor.MeetsAaText("#808080"));
        // A clearly darker blue does pass, so the check is not simply always-false.
        Assert.True(BrandColor.MeetsAaText("#00699E"));
    }

    [Fact]
    public void The_default_accent_does_not_reach_AA_for_normal_text()
    {
        // Documents a real, pre-existing brand gap rather than hiding it: NowOnline Sky Blue with
        // white text is 4.03:1. That satisfies AA for large/bold text and UI components (3:1) but
        // NOT the 4.5:1 needed for normal text, and button labels are 14px. Darkening the accent
        // by ~7% (#007CBC) would clear it, but changing the brand colour is a product decision, so
        // the code reports the shortfall instead of silently altering the palette.
        var ratio = BrandColor.BestTextContrast(BrandColor.DefaultAccent);
        Assert.InRange(ratio, 4.0, 4.1);
        Assert.False(BrandColor.MeetsAaText(BrandColor.DefaultAccent));
        // It must still keep white text: dark ink on sky blue is worse (3.92:1).
        Assert.Equal(BrandColor.OnAccentLight, BrandColor.OnAccent(BrandColor.DefaultAccent));
    }
}
