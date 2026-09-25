namespace Ats.Application.Integration;

public sealed record ReferralToolSettings(string BaseUrl, string ApiKey, string AuthToken, int CustomerId);

public sealed record StatusUpdateRequest(
    int CustomerId, string Code, string ExternalVacancyId, string ExternalCandidateId, string? CandidateStatus);

// Reached=false means a network/timeout error (transient). HttpStatus is 0 when not reached.
// MaybeSent=false only when the request definitely never left (see ReferralToolRules.IsDefinitelyNotSent);
// a timeout or a reply cut off mid-stream may still have been processed by ReferralTool.
public sealed record ReferralCallResult(bool Reached, int HttpStatus, string? Body, bool MaybeSent = true);
