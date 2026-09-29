using System.Text.Json;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Integration;

// Snapshot of a job as ReferralTool's vacancy model sees it, taken when the job changes and sent as-is.
public sealed record VacancyPayload(
    string Id, string Title, string Url, string? Location, string EmploymentType,
    IReadOnlyList<string> Categories, bool Inactive)
{
    public const int MaxTitleLength = 150;

    public static VacancyPayload From(Job job, string careerSiteBaseUrl, string tenantSlug, string? location, string? department) =>
        new(job.ExternalRef,
            job.Title.Length <= MaxTitleLength ? job.Title : job.Title[..MaxTitleLength],
            $"{careerSiteBaseUrl.TrimEnd('/')}/careers/{Uri.EscapeDataString(tenantSlug)}/jobs/{Uri.EscapeDataString(job.ExternalRef)}",
            location,
            job.EmploymentType.ToString(),
            department is null ? [] : [department],
            job.IsDeleted || job.Status != JobStatus.Published);

    // Null for a missing, "null", malformed or incomplete snapshot: a deterministic fault that no retry can fix.
    public static VacancyPayload? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        VacancyPayload? p;
        try { p = JsonSerializer.Deserialize<VacancyPayload>(json); }
        catch (JsonException) { return null; }
        return p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Title) || string.IsNullOrWhiteSpace(p.Url)
            ? null
            : p;
    }
}
