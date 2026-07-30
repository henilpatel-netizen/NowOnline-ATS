using Ats.Application.Organisation;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class OrganisationController : Controller
{
    private readonly IOrganisationReadService _org;
    public OrganisationController(IOrganisationReadService org) => _org = org;

    public async Task<IActionResult> Index()
        => View(new OrganisationViewModel { Overview = await _org.GetAsync() });
}
