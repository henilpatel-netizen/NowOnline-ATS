using Ats.Application.Search;
using Ats.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

// Search spans jobs and candidates, so it needs both view permissions.
[Authorize(Policy = AtsPermission.JobsView)]
[Authorize(Policy = AtsPermission.CandidatesView)]
public class SearchController : Controller
{
    private readonly IGlobalSearchService _search;
    public SearchController(IGlobalSearchService search) => _search = search;

    [HttpGet]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
        => PartialView("_Results", await _search.SearchAsync(q, ct));
}
