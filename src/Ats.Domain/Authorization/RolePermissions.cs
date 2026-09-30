using Ats.Domain.Enums;

namespace Ats.Domain.Authorization;

public static class RolePermissions
{
    // Must stay declared above Map: static initialisers run in textual order.
    private static readonly string[] ReadOnly =
        { AtsPermission.DashboardView, AtsPermission.JobsView, AtsPermission.CandidatesView };

    private static readonly Dictionary<string, HashSet<string>> Map = new(StringComparer.Ordinal)
    {
        [AtsRole.Owner] = [.. AtsPermission.All],
        [AtsRole.Recruiter] =
        [
            .. ReadOnly, AtsPermission.JobsManage, AtsPermission.CandidatesManage, AtsPermission.ApplicationsMove,
            AtsPermission.ResumesDownload, AtsPermission.PipelinesManage, AtsPermission.OrganisationManage,
        ],
        // Not yet limited to the manager's own jobs; phase 3 adds that. No HiringManager user can exist before phase 2.
        [AtsRole.HiringManager] = [.. ReadOnly, AtsPermission.ApplicationsMove, AtsPermission.ResumesDownload],
        [AtsRole.Viewer] = [.. ReadOnly],
    };

    public static bool Has(string? role, string permission) =>
        role is not null && Map.TryGetValue(role, out var granted) && granted.Contains(permission);

    public static string[] RolesWith(string permission) =>
        Map.Where(kv => kv.Value.Contains(permission)).Select(kv => kv.Key).ToArray();
}
