using Ats.Application.Abstractions;
using Ats.Application.Branding;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Branding;

// Registered scoped, so _cached makes this one query per request no matter how many components
// on the page ask for branding.
public sealed class TenantBrandingService : ITenantBrandingService
{
    private readonly AtsDbContext _db;
    private readonly ITenantContext _tenant;
    private TenantBranding? _cached;

    public TenantBrandingService(AtsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<TenantBranding> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var tenantId = _tenant.CurrentTenantId;
        if (tenantId is null) return _cached = Fallback();

        // Tenant is not an ITenantEntity, so it is unfiltered and must be looked up by id.
        var tenant = await _db.Tenants
            .Where(t => t.Id == tenantId.Value)
            .Select(t => new { t.Name, t.Slug })
            .FirstOrDefaultAsync(ct);

        // TenantSettings is tenant-filtered, so no predicate is needed.
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);

        var accent = BrandColor.Normalize(settings?.BrandAccentColor) ?? BrandColor.DefaultAccent;
        var hover = accent == BrandColor.DefaultAccent
            ? BrandColor.DefaultAccentHover
            : BrandColor.Lighten(accent, 0.08) ?? BrandColor.DefaultAccentHover;

        return _cached = new TenantBranding(
            TenantName: tenant?.Name ?? "ATS",
            TenantSlug: tenant?.Slug ?? string.Empty,
            Accent: accent,
            AccentHover: hover,
            SidebarTheme: settings?.BrandSidebarTheme ?? SidebarTheme.Dark,
            CareerHeroHeadline: settings?.CareerHeroHeadline,
            CareerHeroHeadlineOutlined: settings?.CareerHeroHeadlineOutlined,
            CareerHeroIntro: settings?.CareerHeroIntro);
    }

    public async Task UpdateAsync(BrandingInput input, CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstAsync(ct);

        // An invalid colour is stored as null, which resolves back to the default on read.
        settings.BrandAccentColor = BrandColor.Normalize(input.AccentColor);
        settings.BrandSidebarTheme = input.SidebarTheme;
        settings.CareerHeroHeadline = Trimmed(input.CareerHeroHeadline);
        settings.CareerHeroHeadlineOutlined = Trimmed(input.CareerHeroHeadlineOutlined);
        settings.CareerHeroIntro = Trimmed(input.CareerHeroIntro);

        await _db.SaveChangesAsync(ct);
        _cached = null;
    }

    private static string? Trimmed(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static TenantBranding Fallback() => new(
        "ATS", string.Empty, BrandColor.DefaultAccent, BrandColor.DefaultAccentHover,
        SidebarTheme.Dark, null, null, null);
}
