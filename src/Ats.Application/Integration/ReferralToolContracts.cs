namespace Ats.Application.Integration;

public sealed record ReferralToolSettings(string BaseUrl, string ApiKey, string AuthToken, int CustomerId);

public sealed record StatusUpdateRequest(
    int CustomerId, string Code, string ExternalVacancyId, string ExternalCandidateId, string? CandidateStatus);

// Reached=false means a network/timeout error (transient). HttpStatus is 0 when not reached.
public sealed record ReferralCallResult(bool Reached, int HttpStatus, string? Body);
