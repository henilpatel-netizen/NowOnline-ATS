using Ats.Application.Candidates;
using Ats.Application.Common;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public class CandidatesIndexViewModel
{
    public PagedResult<CandidateListItem> Results { get; set; } = default!;
    public string? Q { get; set; }
    public List<SelectListItem> PublishedJobs { get; set; } = new();
}
