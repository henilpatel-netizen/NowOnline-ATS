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
        // Pure read for display: no change tracking needed.
        var query = _db.OutboxMessages.AsNoTracking();
        if (status is OutboxStatus.Pending)
        {
            // Processing is a transient in-flight state (a worker has claimed the message). Users
            // think in Pending/Delivered/Failed, so an in-flight message counts as still pending —
            // otherwise a claimed message disappears from both the tile and this filter.
            query = query.Where(m => m.Status == OutboxStatus.Pending || m.Status == OutboxStatus.Processing);
        }
        else if (status is not null)
        {
            query = query.Where(m => m.Status == status);
        }

        var total = await query.CountAsync(ct);
        var messages = await query.OrderByDescending(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var ids = messages.Select(m => m.Id).ToList();
        var deliveries = await _db.WebhookDeliveries.AsNoTracking()
            .Where(d => ids.Contains(d.OutboxMessageId))
            .ToListAsync(ct);

        var items = messages
            .Select(m => new DeliveryLogEntry(
                m, deliveries.Where(d => d.OutboxMessageId == m.Id).OrderBy(d => d.Id).ToList()))
            .ToList();

        return new PagedResult<DeliveryLogEntry>(items, page, pageSize, total);
    }
}
