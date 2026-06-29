using Ats.Application.Common;
using Ats.Application.Integration;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class DeliveryLogService : IDeliveryLogService
{
    private readonly AtsDbContext _db;
    public DeliveryLogService(AtsDbContext db) => _db = db;

    public async Task<PagedResult<DeliveryLogEntry>> SearchAsync(OutboxStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.OutboxMessages.AsQueryable();
        if (status is not null) query = query.Where(m => m.Status == status);

        var total = await query.CountAsync(ct);
        var messages = await query.OrderByDescending(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var ids = messages.Select(m => m.Id).ToList();
        var deliveries = await _db.WebhookDeliveries
            .Where(d => ids.Contains(d.OutboxMessageId))
            .ToListAsync(ct);

        var items = messages
            .Select(m => new DeliveryLogEntry(
                m, deliveries.Where(d => d.OutboxMessageId == m.Id).OrderBy(d => d.Id).ToList()))
            .ToList();

        return new PagedResult<DeliveryLogEntry>(items, page, pageSize, total);
    }
}
