using System.Globalization;
using Ats.Application.Abstractions;
using Ats.Application.Auditing;
using Ats.Application.Users;
using Ats.Domain.Authorization;
using Ats.Web.Identity;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize(Policy = AtsPermission.ProfileManage)]
public class ProfileController : Controller
{
    private readonly IUserService _users;
    private readonly IIdentityService _identity;
    private readonly ICurrentUser _current;
    private readonly ITenantContext _tenant;
    private readonly IAuditLogger _audit;

    public ProfileController(IUserService users, IIdentityService identity, ICurrentUser current, ITenantContext tenant, IAuditLogger audit)
    {
        _users = users; _identity = identity; _current = current; _tenant = tenant; _audit = audit;
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var userId = _current.UserId!.Value;
        var result = await _users.ChangeOwnPasswordAsync(userId, vm.CurrentPassword, vm.NewPassword);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(vm);
        }

        // The stamp just rotated, so this cookie is now stale: re-issue it without the must-change flag.
        var tenantId = _tenant.CurrentTenantId!.Value;
        var session = await _identity.GetSessionAsync(userId, tenantId);
        if (session is null) return RedirectToAction("Login", "Account");
        await AtsSignIn.SignInAsync(HttpContext, userId, tenantId, session.Role, _current.Name ?? "", session.SecurityStamp, false);

        await _audit.LogAsync("PasswordChanged", "User", userId.ToString(CultureInfo.InvariantCulture), "Changed own password");
        TempData["Success"] = "Password changed.";
        return RedirectToAction("Index", "Dashboard");
    }
}
