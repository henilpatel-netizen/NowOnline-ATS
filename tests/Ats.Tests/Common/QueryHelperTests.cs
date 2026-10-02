using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Common;

public class PagingTests
{
    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(2, 20, 20)]
    [InlineData(3, 25, 50)]
    public void Offset_skips_the_earlier_pages(int page, int pageSize, int expected) =>
        Assert.Equal(expected, Paging.Offset(page, pageSize));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void Offset_treats_a_page_below_one_as_the_first(int page) =>
        Assert.Equal(0, Paging.Offset(page, 20));

    [Fact]
    public void Offset_caps_instead_of_overflowing_on_a_huge_page() =>
        Assert.Equal(int.MaxValue, Paging.Offset(int.MaxValue, 20));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void Clamp_moves_a_page_below_one_to_the_first(int page) =>
        Assert.Equal(1, Paging.Clamp(page, 45, 20));

    [Theory]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void Clamp_moves_a_page_past_the_end_to_the_last(int page) =>
        Assert.Equal(3, Paging.Clamp(page, 45, 20));

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(-1)]
    public void Clamp_returns_page_one_when_there_is_nothing(int page) =>
        Assert.Equal(1, Paging.Clamp(page, 0, 20));

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void Clamp_keeps_the_last_page_when_total_fills_it_exactly(int page, int expected) =>
        Assert.Equal(expected, Paging.Clamp(page, 40, 20));

    [Fact]
    public void Clamp_does_not_overflow_on_a_huge_total() =>
        Assert.Equal(int.MaxValue / 20 + 1, Paging.Clamp(int.MaxValue, int.MaxValue, 20));
}

public class LikePatternTests
{
    [Fact]
    public void Contains_wraps_the_term_in_wildcards() =>
        Assert.Equal("%User%", LikePattern.Contains("User"));

    [Theory]
    [InlineData("50%", "%50[%]%")]
    [InlineData("a_b", "%a[_]b%")]
    [InlineData("[x]", "%[[]x]%")]
    [InlineData("[%_", "%[[][%][_]%")]
    public void Contains_makes_like_metacharacters_literal(string term, string expected) =>
        Assert.Equal(expected, LikePattern.Contains(term));
}
