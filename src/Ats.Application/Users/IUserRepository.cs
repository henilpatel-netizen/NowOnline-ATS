using Ats.Domain.Entities;

namespace Ats.Application.Users;

// Projection for the Users screen: never carries the password hash.
public sealed record UserListItem(int Id, string DisplayName, string Email, string Role, bool IsActive, DateTimeOffset CreatedAt);

public interface IUserRepository
{
    Task<AppUser?> GetAsync(int id, CancellationToken ct = default);
    Task<int> CountActiveOwnersAsync(CancellationToken ct = default);
    // Adds and saves, returning the new id; null when the email is already taken (a concurrent create won the unique index).
    Task<int?> TryAddAsync(AppUser user, CancellationToken ct = default);
    // Saves; false when the save hit the unique email index (a concurrent change took that email).
    Task<bool> TrySaveAsync(CancellationToken ct = default);
    // Marks every hiring team link of the user for removal (saved by the next save); returns how many of those
    // jobs are not deleted.
    Task<int> RemoveHiringTeamLinksAsync(int userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}
