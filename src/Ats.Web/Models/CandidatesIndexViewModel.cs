using Ats.Application.Common;
using Ats.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public class CandidatesIndexViewModel
{
    public PagedResult<Candidate> Results { get; set; } = default!;
    public string? Q { get; set; }
    public List<SelectListItem> PublishedJobs { get; set; } = new();
}
