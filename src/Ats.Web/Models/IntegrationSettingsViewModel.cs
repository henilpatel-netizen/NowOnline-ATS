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
    public int PublishedJobCount { get; set; }

    // Optimistic-concurrency token, round-tripped as base64 through a hidden field.
    public byte[]? RowVersion { get; set; }
}

// Typed replacement for the ViewData["Delivered"]/["Failed"]/["Pending"]/... magic strings the
// Integrations health banner used to rely on: a renamed key failed silently (QUAL-3).
public sealed class IntegrationHealthViewModel
{
    public DateTimeOffset? FeedLastPulledAt { get; init; }
    public int Delivered { get; init; }
    public int Failed { get; init; }
    public int Pending { get; init; }
    public IReadOnlyList<Ats.Application.Integration.DeliveryLogEntry> RecentDeliveries { get; init; }
        = Array.Empty<Ats.Application.Integration.DeliveryLogEntry>();
}
