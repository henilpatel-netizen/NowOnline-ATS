using Ats.Application.Email;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Ats.Infrastructure.Email;

public sealed partial class EmailOutbox : IEmailOutbox
{
    private readonly AtsDbContext _db;
    private readonly ILogger<EmailOutbox> _logger;

    public EmailOutbox(AtsDbContext db, ILogger<EmailOutbox> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Stage(EmailDraft draft, int? tenantId)
    {
        // A stored address the email rules reject must not fail the change that sends the mail. The address is
        // personal data, so it is not logged.
        if (!EmailDraft.HasValidRecipient(draft))
        {
            LogSkipped(_logger, tenantId);
            return;
        }
        EmailDraft.Validate(draft);
        var now = DateTimeOffset.UtcNow;
        _db.EmailMessages.Add(new EmailMessage
        {
            TenantId = tenantId,
            To = draft.To,
            Subject = draft.Subject,
            HtmlBody = draft.HtmlBody,
            TextBody = draft.TextBody,
            Status = OutboxStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now
        });
    }

    public async Task SendAsync(EmailDraft draft, int? tenantId, CancellationToken ct = default)
    {
        Stage(draft, tenantId);
        await _db.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email not queued for tenant {TenantId}: the recipient is not a valid address.")]
    private static partial void LogSkipped(ILogger logger, int? tenantId);
}
