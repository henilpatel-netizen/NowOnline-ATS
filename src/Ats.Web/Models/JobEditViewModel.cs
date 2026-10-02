using System.ComponentModel.DataAnnotations;
using Ats.Application.Jobs;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public class JobEditViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    [Required] public int PipelineTemplateId { get; set; }
    public List<int> HiringManagerIds { get; set; } = new();

    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Locations { get; set; } = new();
    public List<SelectListItem> Pipelines { get; set; } = new();
    // Loaded only for a user who may edit the job.
    public IReadOnlyList<HiringManagerOption> HiringManagerOptions { get; set; } = [];
    // The saved team (edit only).
    public IReadOnlyList<HiringTeamMember> HiringTeam { get; set; } = [];
}
