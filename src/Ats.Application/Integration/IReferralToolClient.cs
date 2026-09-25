namespace Ats.Application.Integration;

public interface IReferralToolClient
{
    // Exists is null unless ReferralTool answered 2xx with { "exists": bool }: false means confirmed
    // not imported, never "unknown".
    Task<(ReferralCallResult Result, bool? Exists)> CheckVacancyExistsAsync(
        ReferralToolSettings settings, string externalVacancyId, CancellationToken ct = default);

    Task<ReferralCallResult> SendStatusUpdateAsync(
        ReferralToolSettings settings, StatusUpdateRequest request, CancellationToken ct = default);
}
