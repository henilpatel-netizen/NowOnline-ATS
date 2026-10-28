using Ats.Application.Branding;
using Ats.Domain.Enums;

namespace Ats.Tests.Fakes;

public sealed class FakeTenantBranding : ITenantBrandingService
{
    public string TenantName { get; set; } = "Acme";

    public Task<TenantBranding> GetAsync(CancellationToken ct = default) =>
        Task.FromResult(new TenantBranding(TenantName, "acme", BrandColor.DefaultAccent, BrandColor.DefaultAccentHover,
            SidebarTheme.Dark, null, null, null));

    public Task UpdateAsync(BrandingInput input, CancellationToken ct = default) => throw new NotSupportedException();
}
