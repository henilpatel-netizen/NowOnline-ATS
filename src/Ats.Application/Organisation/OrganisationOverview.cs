namespace Ats.Application.Organisation;

public sealed record OrgDepartment(int Id, string Name, int JobCount);
public sealed record OrgLocation(int Id, string Name, string? City, int JobCount);
public sealed record OrganisationOverview(
    IReadOnlyList<OrgDepartment> Departments,
    IReadOnlyList<OrgLocation> Locations);

public interface IOrganisationReadService
{
    Task<OrganisationOverview> GetAsync(CancellationToken ct = default);
}
