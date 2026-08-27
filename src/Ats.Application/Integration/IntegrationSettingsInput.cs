namespace Ats.Application.Integration;

public record IntegrationSettingsInput(
    bool IntegrationEnabled,
    string? ReferralToolBaseUrl,
    int? ReferralToolCustomerId,
    string CodeParameterName,
    string? ReferralToolAuthToken,   // null/blank = keep existing
    string? ReferralToolApiKey,      // null/blank = keep existing
    byte[]? RowVersion = null);      // optimistic-concurrency token from page load
