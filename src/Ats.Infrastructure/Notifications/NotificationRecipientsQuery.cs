using Ats.Application.Notifications;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Notifications;

// Normal tenant filter on Users and Jobs; the team comes through db.Jobs, so a soft-deleted job has no team.
public sealed class NotificationRecipientsQuery : INotificationRecipientsQuery
{
    private readonly AtsDbContext _db;
    public NotificationRecipientsQuery(AtsDbContext db) => _db = db;

    public async Task<IReadOnlyList<Recipient>> NewApplicationAsync(int jobId, CancellationToken ct = default) =>
        await NotificationRecipientFilter.NewApplication(_db.Users.AsNoTracking(), Team(jobId)).ToListAsync(ct);

    public async Task<IReadOnlyList<Recipient>> StageChangeAsync(int jobId, CancellationToken ct = default) =>
        await NotificationRecipientFilter.StageChange(_db.Users.AsNoTracking(), Team(jobId)).ToListAsync(ct);

    private IQueryable<int> Team(int jobId) =>
        _db.Jobs.Where(j => j.Id == jobId).SelectMany(j => j.HiringManagers).Select(h => h.UserId);
}
