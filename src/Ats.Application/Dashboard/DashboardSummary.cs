using Ats.Domain.Enums;

namespace Ats.Application.Dashboard;

public sealed record StageCount(string Stage, int Count);
public sealed record SourceSlice(ApplicationOrigin Origin, int Percent);
// RequiredPermission is the permission the Url's page demands; the view hides items the user cannot open.
public sealed record AttentionItem(string Icon, string Tone, string Headline, string Subline, string Url, string RequiredPermission);
public sealed record ActivityItem(string Actor, string Text, DateTimeOffset OccurredAt);

public sealed record IntegrationHealth(
    bool Connected, int? CustomerId, int Delivered24h, int Failed24h, int Pending);

public sealed record DashboardSummary(
    int OpenJobs,
    int ActiveApplications,
    int TotalCandidates,
    int? TimeToHireDays,
    int? OfferAcceptanceRate,
    IReadOnlyList<StageCount> ByStage,
    IReadOnlyList<SourceSlice> Sources,
    IReadOnlyList<AttentionItem> NeedsAttention,
    // Null when the user may not see the tenant activity feed (job-scoped users); the view omits it.
    IReadOnlyList<ActivityItem>? Activity,
    IntegrationHealth Integration);
