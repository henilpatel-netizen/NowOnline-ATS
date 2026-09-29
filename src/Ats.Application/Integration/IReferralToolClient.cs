namespace Ats.Application.Integration;

public interface IReferralToolClient
{
    // Exists is null unless ReferralTool answered 2xx with { "exists": bool }: false means confirmed
    // not there yet in ReferralTool, never "unknown".
    Task<(ReferralCallResult Result, bool? Exists)> CheckVacancyExistsAsync(
        ReferralToolSettings settings, string externalVacancyId, CancellationToken ct = default);

    Task<ReferralCallResult> SendStatusUpdateAsync(
        ReferralToolSettings settings, StatusUpdateRequest request, CancellationToken ct = default);

    // POST /v1.0/vacancy (X-Api-Key identifies the customer). ReferralTool rejects an existing Id with 400.
    Task<ReferralCallResult> CreateVacancyAsync(ReferralToolSettings settings, VacancyPayload vacancy, CancellationToken ct = default);

    // PUT /v1.0/vacancy/{id}: a full replace, so every field is always sent.
    Task<ReferralCallResult> UpdateVacancyAsync(ReferralToolSettings settings, VacancyPayload vacancy, CancellationToken ct = default);
}
