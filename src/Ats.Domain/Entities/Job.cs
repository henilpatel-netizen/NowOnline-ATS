using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class Job : TenantEntity, ISoftDeletable
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public int PipelineTemplateId { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Draft;
    public DateTimeOffset? PublishedAt { get; set; }
    public string ExternalRef { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}
