using Ats.Application.Auditing;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize(Roles = AtsRole.Owner)]
public class AuditController : Controller
{
    private readonly IAuditQuery _audit;
    public AuditController(IAuditQuery audit) => _audit = audit;

    // NOTE: the filter param binds from "act", not "action" — "action" is a reserved MVC route
    // token and would always bind to the current action name ("Index"), silently filtering out
    // every row.
    public async Task<IActionResult> Index(string? q, [FromQuery(Name = "act")] string? actionFilter, string? range, int page = 1)
    {
        if (page < 1) page = 1;
        DateTimeOffset? from = range switch
        {
            "7" => DateTimeOffset.UtcNow.AddDays(-7),
            "30" => DateTimeOffset.UtcNow.AddDays(-30),
            _ => null
        };
        var results = await _audit.SearchAsync(q, actionFilter, from, page, 20);
        var actions = (await _audit.DistinctActionsAsync())
            .Select(a => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(a, a, a == actionFilter)).ToList();
        return View(new Ats.Web.Models.AuditIndexViewModel
        {
            Results = results,
            Q = q,
            Action = actionFilter,
            Range = range,
            Actions = actions
        });
    }
}
