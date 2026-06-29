namespace Ats.Application.Dashboard;

public sealed record StageCount(string Stage, int Count);
public sealed record RecentApplication(string Candidate, string Job, DateTimeOffset AppliedAt);
public sealed record DashboardSummary(
    int PublishedJobs,
    int TotalCandidates,
    int ActiveApplications,
    IReadOnlyList<StageCount> ByStage,
    IReadOnlyList<RecentApplication> Recent);
