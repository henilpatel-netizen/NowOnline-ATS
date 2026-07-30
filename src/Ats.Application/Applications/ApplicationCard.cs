using Ats.Domain.Enums;

namespace Ats.Application.Applications;

public sealed record StageProgressItem(string Name, bool Reached, bool IsCurrent);

public sealed record ApplicationHistoryItem(string Title, string Subtitle, bool IsCurrent);

public sealed record ApplicationCard(
    int ApplicationId,
    string CandidateName,
    string Email,
    string? Phone,
    string JobTitle,
    int JobId,
    string CurrentStageName,
    string? NextStageName,
    ApplicationStatus Status,
    ApplicationOrigin Origin,
    string? ReferralCode,
    DateTimeOffset AppliedAt,
    int DaysInStage,
    string? DeliveryState,       // Delivered / Failed / Pending, or null when no outbox message
    string? ResumeFileName,
    long? ResumeSizeBytes,
    IReadOnlyList<StageProgressItem> Progress,
    IReadOnlyList<ApplicationHistoryItem> History);

public interface IApplicationCardQuery
{
    Task<ApplicationCard?> GetAsync(int applicationId, CancellationToken ct = default);
}
