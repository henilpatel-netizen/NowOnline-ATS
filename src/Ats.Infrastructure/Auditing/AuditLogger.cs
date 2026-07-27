using Ats.Application.Abstractions;
using Ats.Application.Auditing;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Ats.Infrastructure.Auditing;

public sealed class AuditLogger : IAuditLogger
{
    private readonly AtsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(AtsDbContext db, ICurrentUser user, ILogger<AuditLogger> logger)
    {
        _db = db; _user = user; _logger = logger;
    }

    public async Task LogAsync(string action, string entityType, string? entityRef, string summary, CancellationToken ct = default)
    {
        try
        {
            _db.AuditEntries.Add(new AuditEntry
            {
                Action = action,
                EntityType = entityType,
                EntityRef = entityRef,
                Summary = summary,
                UserId = _user.UserId,
                UserName = _user.Name ?? "Unknown",
                OccurredAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Auditing must never break the action it records.
            _logger.LogError(ex, "Failed to write audit entry for {Action}", action);
        }
    }
}
