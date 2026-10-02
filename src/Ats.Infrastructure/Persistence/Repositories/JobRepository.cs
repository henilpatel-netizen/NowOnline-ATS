using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class JobRepository : IJobRepository
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const string TeamIndex = "IX_JobHiringManagers_TenantId_JobId_UserId";
    private const string JobNumberIndex = "IX_Jobs_TenantId_ExternalRef";

    private readonly AtsDbContext _db;
    public JobRepository(AtsDbContext db) => _db = db;

    public Task<List<Job>> ListAsync(CancellationToken ct = default) =>
        _db.Jobs.OrderByDescending(j => j.Id).ToListAsync(ct);


    public Task<Job?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public Task<Job?> GetWithHiringManagersAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.Include(j => j.HiringManagers).FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<IReadOnlyList<int>> FindAssignableHiringManagerIdsAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default) =>
        await _db.Users
            .Where(u => userIds.Contains(u.Id) && u.IsActive && u.Role == AtsRole.HiringManager)
            .Select(u => u.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<int, string>> GetUserNamesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default) =>
        await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

    public Task<bool> IsAssignedToAsync(int jobId, int userId, CancellationToken ct = default) =>
        _db.Jobs.AssignedTo(userId).AnyAsync(j => j.Id == jobId, ct);

    public async Task AddAsync(Job job, CancellationToken ct = default) =>
        await _db.Jobs.AddAsync(job, ct);

    // Increments the current tenant's LastJobNumber and returns the new value.
    public async Task<int> NextJobNumberAsync(CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstAsync(ct); // tenant-filtered to the current tenant
        settings.LastJobNumber += 1;
        return settings.LastJobNumber;
    }

    public Task<bool> PipelineExistsAsync(int pipelineTemplateId, CancellationToken ct = default) =>
        _db.PipelineTemplates.AnyAsync(t => t.Id == pipelineTemplateId, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task<bool> TrySaveNewJobAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ViolatesUniqueIndex(ex, JobNumberIndex))
        {
            Detach(ex);
            return false;
        }
    }

    public async Task<bool> TrySaveTeamChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ViolatesUniqueIndex(ex, TeamIndex))
        {
            Detach(ex);
            return false;
        }
    }

    // Detach so a later SaveChanges in this scope does not retry the rejected changes.
    private static void Detach(DbUpdateException ex)
    {
        foreach (var entry in ex.Entries)
            entry.State = EntityState.Detached;
    }

    private static bool ViolatesUniqueIndex(DbUpdateException ex, string index) =>
        ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql
        && sql.Message.Contains(index, StringComparison.Ordinal);
}
