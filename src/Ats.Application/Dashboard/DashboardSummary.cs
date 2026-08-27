using Ats.Domain.Enums;

namespace Ats.Application.Dashboard;

public sealed record StageCount(string Stage, int Count);
public sealed record SourceSlice(ApplicationOrigin Origin, int Percent);
public sealed record AttentionItem(string Icon, string Tone, string Headline, string Subline, string Url);
public sealed record ActivityItem(string Actor, string Text, DateTimeOffset OccurredAt);

public sealed record IntegrationHealth(
    bool Connected, int? CustomerId, DateTimeOffset? FeedLastPulledAt,
    int Delivered24h, int Failed24h, int Pending);

public sealed record DashboardSummary(
    int OpenJobs,
    int ActiveApplications,
    int TotalCandidates,
    int? TimeToHireDays,
    int? OfferAcceptanceRate,
    IReadOnlyList<StageCount> ByStage,
    IReadOnlyList<SourceSlice> Sources,
    IReadOnlyList<AttentionItem> NeedsAttention,
    IReadOnlyList<ActivityItem> Activity,
    IntegrationHealth Integration);
