using Ats.Application.Users;
using Ats.Domain.Entities;
using Xunit;

namespace Ats.Tests.Users;

public class UserListFilterTests
{
    private static readonly AppUser[] Users =
    [
        new() { Id = 1, DisplayName = "Zoe Owner", Email = "zoe@acme.test", IsActive = true },
        new() { Id = 2, DisplayName = "Adam Gone", Email = "adam@acme.test", IsActive = false },
        new() { Id = 3, DisplayName = "Bea Viewer", Email = "bea@acme.test", IsActive = true },
        new() { Id = 4, DisplayName = "Bea Viewer", Email = "bea2@other.test", IsActive = true },
    ];

    private static int[] Ids(UserStatusFilter status, string? search = null) =>
        Users.AsQueryable().ApplyListFilter(status, search).Select(u => u.Id).ToArray();

    [Fact]
    public void Active_shows_only_active_users_by_name_then_id() =>
        Assert.Equal([3, 4, 1], Ids(UserStatusFilter.Active));

    [Fact]
    public void Deactivated_shows_only_deactivated_users() =>
        Assert.Equal([2], Ids(UserStatusFilter.Deactivated));

    [Fact]
    public void All_lists_active_users_first_then_by_name() =>
        Assert.Equal([3, 4, 1, 2], Ids(UserStatusFilter.All));

    [Fact]
    public void An_unknown_status_falls_back_to_active() =>
        Assert.Equal([3, 4, 1], Ids((UserStatusFilter)42));

    [Fact]
    public void Search_matches_the_name_or_the_email()
    {
        Assert.Equal([1], Ids(UserStatusFilter.All, "Zoe"));
        Assert.Equal([4], Ids(UserStatusFilter.All, "other.test"));
    }

    [Fact]
    public void Search_is_trimmed_and_combined_with_the_status()
    {
        Assert.Equal([2], Ids(UserStatusFilter.All, "  adam@  "));
        Assert.Empty(Ids(UserStatusFilter.Active, "adam@"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_search_is_ignored(string? search) =>
        Assert.Equal([3, 4, 1, 2], Ids(UserStatusFilter.All, search));
}
