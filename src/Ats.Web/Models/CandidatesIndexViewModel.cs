using Ats.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public class CandidatesIndexViewModel
{
    public List<Candidate> Candidates { get; set; } = new();
    public List<SelectListItem> PublishedJobs { get; set; } = new();
}
