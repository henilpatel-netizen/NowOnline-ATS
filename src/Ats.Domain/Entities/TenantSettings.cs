using Ats.Domain.Common;

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
}
