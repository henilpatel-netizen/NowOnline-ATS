namespace Ats.Application.Auditing;

public interface IAuditLogger
{
    Task LogAsync(string action, string entityType, string? entityRef, string summary, CancellationToken ct = default);
}
