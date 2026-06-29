using Ats.Application.Integration;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class DeliveryLogService : IDeliveryLogService
{
    private readonly AtsDbContext _db;
    public DeliveryLogService(AtsDbContext db) => _db = db;

    public async Task<List<DeliveryLogEntry>> RecentAsync(int take = 200, CancellationToken ct = default)
    {
        var messages = await _db.OutboxMessages
            .OrderByDescending(m => m.Id)
            .Take(take)
            .ToListAsync(ct);

        var ids = messages.Select(m => m.Id).ToList();
        var deliveries = await _db.WebhookDeliveries
            .Where(d => ids.Contains(d.OutboxMessageId))
            .ToListAsync(ct);

        return messages
            .Select(m => new DeliveryLogEntry(
                m,
                deliveries.Where(d => d.OutboxMessageId == m.Id).OrderBy(d => d.Id).ToList()))
            .ToList();
    }
}
