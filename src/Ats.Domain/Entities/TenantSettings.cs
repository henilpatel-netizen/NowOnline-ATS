using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class TenantSettings : TenantEntity
{
    public bool IntegrationEnabled { get; set; }
    public string? ReferralToolBaseUrl { get; set; }
    public string? ReferralToolAuthToken { get; set; }
    public int? ReferralToolCustomerId { get; set; }
    public string CodeParameterName { get; set; } = "ref";
    public string? FeedApiKeyHash { get; set; }
    public int LastJobNumber { get; set; }
    public string? ReferralToolApiKey { get; set; }

    // Branding. Null means "use the NowOnline default", so a tenant that has never opened the
    // branding screen renders exactly as it did before these columns existed.
    public string? BrandAccentColor { get; set; }
    public SidebarTheme? BrandSidebarTheme { get; set; }
    public string? CareerHeroHeadline { get; set; }
    public string? CareerHeroHeadlineOutlined { get; set; }
    public string? CareerHeroIntro { get; set; }

    // Telemetry for the integration health panels. Written by the vacancy feed endpoint.
    public DateTimeOffset? FeedLastPulledAt { get; set; }
}
