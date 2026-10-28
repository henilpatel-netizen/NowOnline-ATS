using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

// Not an ITenantEntity: staged from anonymous requests (forgot password) and read only by the Worker.
public class EmailMessage
{
    public int Id { get; set; }
    // Informational (logs); never a filter.
    public int? TenantId { get; set; }
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    // Both bodies are cleared once the message is finished.
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    // Never contains the body.
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
