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
}
