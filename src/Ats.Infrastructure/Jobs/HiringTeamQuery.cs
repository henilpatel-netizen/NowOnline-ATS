using Ats.Application.Jobs;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Jobs;

public sealed class HiringTeamQuery : IHiringTeamQuery
{
    private readonly AtsDbContext _db;
    public HiringTeamQuery(AtsDbContext db) => _db = db;

    public async Task<IReadOnlyList<HiringManagerOption>> AssignableAsync(CancellationToken ct = default) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == AtsRole.HiringManager)
            .OrderBy(u => u.DisplayName)
            .Select(u => new HiringManagerOption(u.Id, u.DisplayName, u.Email))
            .ToListAsync(ct);

    // Through Jobs, so a soft-deleted job has no team.
    public async Task<IReadOnlyList<HiringTeamMember>> TeamAsync(int jobId, CancellationToken ct = default) =>
        await _db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .SelectMany(j => j.HiringManagers)
            .Join(_db.Users, h => h.UserId, u => u.Id, (h, u) => u)
            .OrderBy(u => u.DisplayName)
            .Select(u => new HiringTeamMember(u.Id, u.DisplayName, u.Email, u.IsActive && u.Role == AtsRole.HiringManager))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AssignedJob>> JobsForAsync(int userId, CancellationToken ct = default) =>
        await _db.Jobs.AsNoTracking()
            .AssignedTo(userId)
            .OrderBy(j => j.Title)
            .Select(j => new AssignedJob(j.Id, j.Title, j.Status))
            .ToListAsync(ct);
}
