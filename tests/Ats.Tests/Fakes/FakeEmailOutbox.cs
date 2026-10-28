using Ats.Application.Email;

namespace Ats.Tests.Fakes;

public sealed class FakeEmailOutbox : IEmailOutbox
{
    public List<(EmailDraft Draft, int? TenantId)> Staged { get; } = new();
    public int SendCount { get; private set; }
    // Drafts a save has committed. Wire Commit to a fake repository's Saved event to model the shared unit of work:
    // a draft staged after the last save, or before a failed save, never gets here.
    public List<(EmailDraft Draft, int? TenantId)> Committed { get; } = new();
    private readonly List<(EmailDraft Draft, int? TenantId)> _pending = new();

    // Mirrors EmailOutbox: an unusable recipient is skipped, any other invalid draft throws.
    public void Stage(EmailDraft draft, int? tenantId)
    {
        if (!EmailDraft.HasValidRecipient(draft)) return;
        EmailDraft.Validate(draft);
        Staged.Add((draft, tenantId));
        _pending.Add((draft, tenantId));
    }

    public Task SendAsync(EmailDraft draft, int? tenantId, CancellationToken ct = default)
    {
        Stage(draft, tenantId);
        Commit();
        SendCount++;
        return Task.CompletedTask;
    }

    public void Commit()
    {
        Committed.AddRange(_pending);
        _pending.Clear();
    }
}
