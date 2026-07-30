using Ats.Domain.Enums;

namespace Ats.Application.Branding;

public sealed record BrandingInput(
    string? AccentColor,
    SidebarTheme SidebarTheme,
    string? CareerHeroHeadline,
    string? CareerHeroHeadlineOutlined,
    string? CareerHeroIntro);

public interface ITenantBrandingService
{
    // Resolved branding for the current tenant, with NowOnline defaults substituted for nulls.
    // Cached for the lifetime of the request.
    Task<TenantBranding> GetAsync(CancellationToken ct = default);

    Task UpdateAsync(BrandingInput input, CancellationToken ct = default);
}
