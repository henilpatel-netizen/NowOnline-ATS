using Ats.Domain.Entities;
using Ats.Web.Models.Board;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public record BoardColumn(PipelineStage Stage, int StageIndex, int StageCount, List<BoardCardModel> Cards);

public class BoardViewModel
{
    public Job Job { get; set; } = default!;
    public List<BoardColumn> Columns { get; set; } = new();
    public string? Error { get; set; }
    public List<SelectListItem> CandidateOptions { get; set; } = new();

    // Stats strip (prototype L420-425).
    public int InProcess { get; set; }
    public int? AvgDaysInStage { get; set; }
    public int FromReferral { get; set; }
    public int OldestDays { get; set; }
}
