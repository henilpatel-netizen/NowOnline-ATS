using Ats.Domain.Entities;

namespace Ats.Web.Models;

public class ApplicationDetailsViewModel
{
    public JobApplication Application { get; set; } = default!;
    public string CandidateName { get; set; } = "";
    public List<PipelineStage> Stages { get; set; } = new();
    public List<ApplicationEvent> Events { get; set; } = new();
    public Ats.Application.Applications.ApplicationCard? Card { get; set; }

    public string StageName(int stageId) => Stages.FirstOrDefault(s => s.Id == stageId)?.Name ?? $"#{stageId}";
}
