using Ats.Application.Users;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Tests.Fakes;

public sealed class FakeUserRepository : IUserRepository
{
    public List<AppUser> Users { get; } = new();
    public int SaveCount { get; private set; }
    public int TransactionCount { get; private set; }
    public bool RejectNextAddAsDuplicate { get; set; }
    public bool RejectNextSaveAsDuplicate { get; set; }

    public Task<AppUser?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<int> CountActiveOwnersAsync(CancellationToken ct = default) =>
        Task.FromResult(Users.Count(u => u.IsActive && u.Role == AtsRole.Owner));

    public Task<int?> TryAddAsync(AppUser user, CancellationToken ct = default)
    {
        if (RejectNextAddAsDuplicate)
        {
            RejectNextAddAsDuplicate = false;
            return Task.FromResult<int?>(null);
        }
        user.Id = Users.Count == 0 ? 1 : Users.Max(u => u.Id) + 1;
        Users.Add(user);
        SaveCount++;
        return Task.FromResult<int?>(user.Id);
    }

    public Task<bool> TrySaveAsync(CancellationToken ct = default)
    {
        if (RejectNextSaveAsDuplicate)
        {
            RejectNextSaveAsDuplicate = false;
            return Task.FromResult(false);
        }
        SaveCount++;
        return Task.FromResult(true);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        TransactionCount++;
        return work(ct);
    }
}
