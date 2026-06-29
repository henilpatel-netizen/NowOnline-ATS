using Ats.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public record BoardCard(int ApplicationId, string CandidateName, string RowVersion);
public record BoardColumn(PipelineStage Stage, List<BoardCard> Cards);

public class BoardViewModel
{
    public Job Job { get; set; } = default!;
    public List<BoardColumn> Columns { get; set; } = new();
    public string? Error { get; set; }
    public List<SelectListItem> CandidateOptions { get; set; } = new();
}
