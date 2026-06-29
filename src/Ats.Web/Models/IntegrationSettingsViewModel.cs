using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class IntegrationSettingsViewModel
{
    public bool IntegrationEnabled { get; set; }
    [StringLength(300)] public string? ReferralToolBaseUrl { get; set; }
    public int? ReferralToolCustomerId { get; set; }
    [Required, StringLength(40)] public string CodeParameterName { get; set; } = "ref";

    // Secrets: blank means keep the stored value.
    [StringLength(500)] public string? ReferralToolAuthToken { get; set; }
    [StringLength(500)] public string? ReferralToolApiKey { get; set; }

    // Display-only flags so the view can show "configured" without revealing secrets.
    public bool HasAuthToken { get; set; }
    public bool HasApiKey { get; set; }
    public bool HasFeedKey { get; set; }
}
