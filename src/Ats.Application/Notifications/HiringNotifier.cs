using Ats.Application.Abstractions;
using Ats.Application.Branding;
using Ats.Application.Email;
using Ats.Application.Integration;

namespace Ats.Application.Notifications;

public sealed class HiringNotifier : IHiringNotifier
{
    private readonly INotificationRecipientsQuery _recipients;
    private readonly IEmailOutbox _outbox;
    private readonly ITenantBrandingService _tenant;   // tenant name, read under the tenant filter
    private readonly ITenantContext _tenantContext;
    private readonly string _baseUrl;

    public HiringNotifier(INotificationRecipientsQuery recipients, IEmailOutbox outbox, ITenantBrandingService tenant,
        ITenantContext tenantContext, IntegrationOptions options)
    {
        _recipients = recipients; _outbox = outbox; _tenant = tenant; _tenantContext = tenantContext;
        _baseUrl = options.CareerSiteBaseUrl.TrimEnd('/');
    }

    public async Task StageNewApplicationAsync(int jobId, string jobTitle, string candidateName, CancellationToken ct = default)
    {
        var recipients = await _recipients.NewApplicationAsync(jobId, ct);
        await StageAsync(recipients, null, jobId,
            (to, tenantName, link) => EmailTemplates.NewApplication(to, tenantName, candidateName, jobTitle, link), ct);
    }

    public async Task StageStageChangedAsync(int jobId, string jobTitle, string candidateName, string stageName,
        int? movedByUserId, CancellationToken ct = default)
    {
        var recipients = await _recipients.StageChangeAsync(jobId, ct);
        await StageAsync(recipients, movedByUserId, jobId,
            (to, tenantName, link) => EmailTemplates.StageChanged(to, tenantName, candidateName, jobTitle, stageName, link), ct);
    }

    private async Task StageAsync(IReadOnlyList<Recipient> recipients, int? excludeUserId, int jobId,
        Func<string, string, string, EmailDraft> render, CancellationToken ct)
    {
        var targets = recipients.Where(r => r.UserId != excludeUserId).DistinctBy(r => r.UserId).ToList();
        if (targets.Count == 0) return;

        var tenantName = (await _tenant.GetAsync(ct)).TenantName;
        var link = $"{_baseUrl}/Board?jobId={jobId}";
        foreach (var r in targets)
            _outbox.Stage(render(r.Email, tenantName, link), _tenantContext.CurrentTenantId);
    }
}
