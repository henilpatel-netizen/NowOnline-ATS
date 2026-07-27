namespace Ats.Application.Integration;

public interface IReferralToolClient
{
    Task<(ReferralCallResult Result, bool Exists)> CheckVacancyExistsAsync(
        ReferralToolSettings settings, string externalVacancyId, CancellationToken ct = default);

    Task<ReferralCallResult> SendStatusUpdateAsync(
        ReferralToolSettings settings, StatusUpdateRequest request, CancellationToken ct = default);
}
