namespace Ats.Application.Email;

public interface IEmailOutbox
{
    // Saved by the caller's next SaveChanges, in the same unit of work as the change that caused it. A draft
    // whose recipient fails EmailDraft.HasValidRecipient is skipped with a warning; any other invalid draft throws.
    void Stage(EmailDraft draft, int? tenantId);

    Task SendAsync(EmailDraft draft, int? tenantId, CancellationToken ct = default);
}
