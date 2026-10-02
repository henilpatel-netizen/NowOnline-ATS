using Ats.Application.Common;
using Ats.Domain.Enums;

namespace Ats.Application.Jobs;

// HiringManagerIds replaces the whole team; an empty list clears it.
public record JobInput(
    int? Id, string Title, string? Description, int? DepartmentId, int? LocationId,
    EmploymentType EmploymentType, int PipelineTemplateId, IReadOnlyList<int> HiringManagerIds);

// Who joined and left the hiring team in one save, by name, for the audit summary.
public sealed record HiringTeamChange(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    public static readonly HiringTeamChange None = new([], []);

    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    public string Describe()
    {
        if (IsEmpty) return "";
        var parts = new List<string>(2);
        if (Added.Count > 0) parts.Add("added " + string.Join(", ", Added));
        if (Removed.Count > 0) parts.Add("removed " + string.Join(", ", Removed));
        return "hiring team: " + string.Join("; ", parts);
    }
}

public sealed record JobSaveResult(bool Succeeded, string? Error, HiringTeamChange Team) : OperationResult(Succeeded, Error)
{
    public static JobSaveResult Saved(HiringTeamChange team) => new(true, null, team);
    public static JobSaveResult Failed(string error) => new(false, error, HiringTeamChange.None);
}

// The hiring-team field and the places that show a team or a manager's jobs.
public sealed record HiringManagerOption(int Id, string Name, string Email);

// IsAssignable is false for a member who has since been deactivated or given another role.
public sealed record HiringTeamMember(int Id, string Name, string Email, bool IsAssignable);

public sealed record AssignedJob(int Id, string Title, JobStatus Status);

// Read-only queries on the tenant-filtered context. Callers decide whether the job is visible to the
// user first; these do not apply the job scope.
public interface IHiringTeamQuery
{
    // Active users whose role is HiringManager, by name.
    Task<IReadOnlyList<HiringManagerOption>> AssignableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HiringTeamMember>> TeamAsync(int jobId, CancellationToken ct = default);
    Task<IReadOnlyList<AssignedJob>> JobsForAsync(int userId, CancellationToken ct = default);
}
