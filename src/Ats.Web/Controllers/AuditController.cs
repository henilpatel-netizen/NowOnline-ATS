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

    public async Task<IActionResult> Index() => View(await _audit.RecentAsync());
}
