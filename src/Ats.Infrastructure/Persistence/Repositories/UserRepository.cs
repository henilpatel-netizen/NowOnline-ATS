using System.Data;
using Ats.Application.Users;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const string EmailIndex = "IX_Users_Email";

    private readonly AtsDbContext _db;
    public UserRepository(AtsDbContext db) => _db = db;

    public Task<AppUser?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<int> CountActiveOwnersAsync(CancellationToken ct = default) =>
        _db.Users.CountAsync(u => u.IsActive && u.Role == AtsRole.Owner, ct);

    public async Task<int?> TryAddAsync(AppUser user, CancellationToken ct = default)
    {
        await _db.Users.AddAsync(user, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
            return user.Id;
        }
        catch (DbUpdateException ex) when (IsDuplicateEmail(ex))
        {
            // Detach so a later SaveChanges in this scope does not retry the rejected insert.
            _db.Entry(user).State = EntityState.Detached;
            return null;
        }
    }

    public async Task<bool> TrySaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateEmail(ex))
        {
            // Detach so a later SaveChanges in this scope does not retry the rejected update.
            foreach (var entry in ex.Entries)
                entry.State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    private static bool IsDuplicateEmail(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql
        && sql.Message.Contains(EmailIndex, StringComparison.Ordinal);

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        // EnableRetryOnFailure forbids a user transaction outside the execution strategy. Serializable
        // so a count-then-update (last active Owner) holds range locks until commit.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // A retry re-runs the work after a rollback, but tracked users would still carry the failed
            // attempt's edits (a tracking query does not overwrite them). Detach only users, so the work
            // reloads them from the database and unrelated pending changes in this scope survive.
            foreach (var entry in _db.ChangeTracker.Entries<AppUser>().ToList())
                entry.State = EntityState.Detached;

            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await work(ct);
            await tx.CommitAsync(ct);
            return result;
        });
    }
}
