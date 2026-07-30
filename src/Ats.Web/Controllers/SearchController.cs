using Ats.Application.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

// Authorization in this app is per-controller (see JobsController's [Authorize] and
// IntegrationController's [Authorize(Roles = Owner)]). Search spans jobs, candidates and referral
// codes, so it takes the bare [Authorize] with no role gate.
[Authorize]
public class SearchController : Controller
{
    private readonly IGlobalSearchService _search;
    public SearchController(IGlobalSearchService search) => _search = search;

    [HttpGet]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
        => PartialView("_Results", await _search.SearchAsync(q, ct));
}
