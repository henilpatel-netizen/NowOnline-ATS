namespace Ats.Application.Search;

public sealed record JobHit(int Id, string Title, string ExternalRef, string Status);
public sealed record CandidateHit(int Id, string FullName, string Email);
public sealed record ApplicationHit(int Id, string CandidateName, string JobTitle, string ReferralCode);

public sealed record SearchResults(
    IReadOnlyList<JobHit> Jobs,
    IReadOnlyList<CandidateHit> Candidates,
    IReadOnlyList<ApplicationHit> Applications)
{
    public bool IsEmpty => Jobs.Count == 0 && Candidates.Count == 0 && Applications.Count == 0;

    public static SearchResults Empty { get; } = new([], [], []);
}
