namespace Ats.Application.Shell;

// Counts and flags the app shell needs on every authenticated page. Resolved once per request so
// putting them in the sidebar and topbar does not multiply queries across the app.
public sealed record ShellSummary(
    int OpenJobs,
    int Candidates,
    int FailedDeliveries,
    int IdleApplications,
    int StaleDrafts)
{
    public int AttentionCount => FailedDeliveries + IdleApplications + StaleDrafts;
    public bool HasAttention => AttentionCount > 0;
    public bool IntegrationUnhealthy => FailedDeliveries > 0;

    public static ShellSummary Empty { get; } = new(0, 0, 0, 0, 0);
}
