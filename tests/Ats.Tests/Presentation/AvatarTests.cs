using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Presentation;

public class AvatarTests
{
    [Theory]
    [InlineData("Milan Verhoeven", "MV")]
    [InlineData("Fatima El Amrani", "FE")]   // first two tokens, not first + last
    [InlineData("Iris Draaijer", "ID")]
    [InlineData("sanne de vries", "SD")]
    [InlineData("Madonna", "MA")]            // single token: first two letters
    [InlineData("  Bram   Kooijman  ", "BK")]
    [InlineData("X", "X")]
    public void Initials_are_derived_from_the_name(string name, string expected)
    {
        Assert.Equal(expected, AvatarPalette.Initials(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Initials_fall_back_for_a_missing_name(string? name)
    {
        Assert.Equal("?", AvatarPalette.Initials(name));
    }

    [Fact]
    public void Colour_is_stable_for_the_same_name()
    {
        Assert.Equal(AvatarPalette.For("Milan Verhoeven"), AvatarPalette.For("Milan Verhoeven"));
    }

    [Fact]
    public void Colour_ignores_case_and_surrounding_whitespace()
    {
        Assert.Equal(AvatarPalette.For("Milan Verhoeven"), AvatarPalette.For(" milan verhoeven "));
    }

    [Fact]
    public void Colour_comes_from_the_design_palette()
    {
        Assert.Contains(AvatarPalette.For("Milan Verhoeven"), AvatarPalette.Pairs);
    }

    [Fact]
    public void Different_names_spread_across_the_palette()
    {
        var names = new[]
        {
            "Milan Verhoeven", "Ravi Menon", "Anneke Wolters", "Joost Bakker",
            "Fatima El Amrani", "Iris Draaijer", "Bram Kooijman", "Sofia Marchetti",
            "Tim Hofstra", "Sanne de Vries"
        };
        var distinct = names.Select(AvatarPalette.For).Distinct().Count();
        Assert.True(distinct >= 3, $"expected the palette to spread, got {distinct} distinct pairs");
    }

    [Fact]
    public void Missing_name_uses_the_neutral_pair()
    {
        Assert.Equal(AvatarPalette.Neutral, AvatarPalette.For(null));
    }
}
