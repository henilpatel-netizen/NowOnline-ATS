using Ats.Domain.Enums;

namespace Ats.Application.Jobs;

public record JobInput(
    int? Id, string Title, string? Description, int? DepartmentId, int? LocationId,
    EmploymentType EmploymentType, int PipelineTemplateId);
