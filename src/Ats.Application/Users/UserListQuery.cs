using Ats.Application.Common;
using Ats.Domain.Entities;

namespace Ats.Application.Users;

// Active is the default (0) so a URL without a status shows active users.
public enum UserStatusFilter { Active, Deactivated, All }

public interface IUserListQuery
{
    Task<PagedResult<UserListItem>> SearchAsync(UserStatusFilter status, string? search, int page, int pageSize, CancellationToken ct = default);
    // Tenant-wide, whatever the screen is filtered to.
    Task<int> CountActiveAsync(CancellationToken ct = default);
}

public static class UserListFilter
{
    // Order: active users first (only visible under All), then display name, then id so paging is stable.
    // Contains is sent as a parameter, so % _ [ in the search are matched literally; the column
    // collation makes it case-insensitive.
    public static IQueryable<AppUser> ApplyListFilter(this IQueryable<AppUser> users, UserStatusFilter status, string? search)
    {
        users = status switch
        {
            UserStatusFilter.All => users,
            UserStatusFilter.Deactivated => users.Where(u => !u.IsActive),
            _ => users.Where(u => u.IsActive),
        };
        var s = search?.Trim();
        if (!string.IsNullOrEmpty(s))
            users = users.Where(u => u.DisplayName.Contains(s) || u.Email.Contains(s));
        return users.OrderByDescending(u => u.IsActive).ThenBy(u => u.DisplayName).ThenBy(u => u.Id);
    }
}
