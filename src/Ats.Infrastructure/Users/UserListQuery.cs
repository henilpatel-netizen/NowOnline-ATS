using Ats.Application.Common;
using Ats.Application.Users;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Users;

// Read projection for the Users screen: filtered and paged on the server, never selects the password hash.
public sealed class UserListQuery : IUserListQuery
{
    private readonly AtsDbContext _db;
    public UserListQuery(AtsDbContext db) => _db = db;

    public async Task<PagedResult<UserListItem>> SearchAsync(UserStatusFilter status, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.Users.AsNoTracking().ApplyListFilter(status, search);
        var total = await q.CountAsync(ct);
        page = Paging.Clamp(page, total, pageSize);
        var items = await q
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .Select(u => new UserListItem(u.Id, u.DisplayName, u.Email, u.Role, u.IsActive, u.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<UserListItem>(items, page, pageSize, total);
    }

    public Task<int> CountActiveAsync(CancellationToken ct = default) => _db.Users.CountAsync(u => u.IsActive, ct);
}
