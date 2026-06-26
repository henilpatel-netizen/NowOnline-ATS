using Ats.Application.Abstractions;
using Ats.Domain.Entities;

namespace Ats.Application.Tenancy;

public interface ITenantOnboardingService
{
    Task<RegisterTenantResult> RegisterAsync(RegisterTenantInput input, CancellationToken ct = default);
}

public sealed class TenantOnboardingService : ITenantOnboardingService
{
    private readonly IOnboardingStore _store;
    private readonly IIdentityService _identity;

    public TenantOnboardingService(IOnboardingStore store, IIdentityService identity)
    {
        _store = store;
        _identity = identity;
    }

    public async Task<RegisterTenantResult> RegisterAsync(RegisterTenantInput input, CancellationToken ct = default)
    {
        var slug = input.Slug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(slug) || ReservedSlugs.IsReserved(slug))
            return new RegisterTenantResult(false, 0, 0, "That URL slug is not allowed.");

        if (await _store.SlugExistsAsync(slug, ct))
            return new RegisterTenantResult(false, 0, 0, "That URL slug is already taken.");

        var tenant = new Tenant { Name = input.CompanyName.Trim(), Slug = slug };
        var settings = new TenantSettings { CodeParameterName = "ref" };
        var defaultTemplate = BuildDefaultPipeline();
        var ownerHash = _identity.HashPassword(input.Password);

        var (tenantId, ownerId) = await _store.CreateTenantGraphAsync(
            tenant, settings, defaultTemplate, input.OwnerName.Trim(), input.OwnerEmail.Trim().ToLowerInvariant(), ownerHash, ct);

        return new RegisterTenantResult(true, tenantId, ownerId, null);
    }

    private static PipelineTemplate BuildDefaultPipeline()
    {
        return new PipelineTemplate
        {
            Name = "Standard hiring",
            Stages = new List<PipelineStage>
            {
                new() { Name = "Applied",        Order = 1 },
                new() { Name = "1st Interview",  Order = 2 },
                new() { Name = "2nd Interview",  Order = 3 },
                new() { Name = "Hired",          Order = 4, IsTerminal = true, TerminalOutcome = StageOutcome.Hired },
                new() { Name = "Rejected",       Order = 5, IsTerminal = true, TerminalOutcome = StageOutcome.Rejected },
            }
        };
    }
}
