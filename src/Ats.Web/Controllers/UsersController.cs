using System.Globalization;
using Ats.Application.Abstractions;
using Ats.Application.Auditing;
using Ats.Application.Jobs;
using Ats.Application.Users;
using Ats.Domain.Authorization;
using Ats.Domain.Enums;
using Ats.Web.Identity;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize(Policy = AtsPermission.UsersManage)]
public class UsersController : Controller
{
    private const string DetailsPrefix = "Details";
    private const string ResetPrefix = "Reset";

    private const int PageSize = 20;

    private readonly IUserService _users;
    private readonly IUserListQuery _userList;
    private readonly IIdentityService _identity;
    private readonly ICurrentUser _current;
    private readonly ITenantContext _tenant;
    private readonly IHiringTeamQuery _hiringTeam;
    private readonly IAuditLogger _audit;

    public UsersController(IUserService users, IUserListQuery userList, IIdentityService identity, ICurrentUser current, ITenantContext tenant,
        IHiringTeamQuery hiringTeam, IAuditLogger audit)
    {
        _users = users; _userList = userList; _identity = identity; _current = current; _tenant = tenant; _hiringTeam = hiringTeam; _audit = audit;
    }

    private int Me => _current.UserId!.Value;

    public async Task<IActionResult> Index(string? q, UserStatusFilter status = UserStatusFilter.Active, int page = 1)
    {
        var results = await _userList.SearchAsync(status, q, page, PageSize);
        return View(new UsersIndexViewModel(results, await _userList.CountActiveAsync(), q, status, Me));
    }

    [HttpGet]
    public IActionResult Create() => View(new UserCreateViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(UserCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var (result, userId) = await _users.CreateAsync(new CreateUserInput(vm.DisplayName, vm.Email, vm.Role, vm.TemporaryPassword));
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(vm);
        }
        await _audit.LogAsync("UserCreated", "User", Ref(userId!.Value), $"Added '{vm.Email.Trim().ToLowerInvariant()}' as {vm.Role}");
        TempData["Success"] = "User added. Share the temporary password securely; they must change it when they first sign in.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var stored = await _users.GetAsync(id);
        return stored is null ? NotFound() : await EditView(stored);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = DetailsPrefix)] UserEditViewModel vm)
    {
        var stored = await _users.GetAsync(id);
        if (stored is null) return NotFound();

        // The own-record form posts the name only; the service refuses any other change to self anyway.
        var isMe = id == Me;
        if (isMe)
        {
            vm.Email = stored.Email;
            vm.Role = stored.Role;
            ModelState.Remove($"{DetailsPrefix}.{nameof(vm.Email)}");
            ModelState.Remove($"{DetailsPrefix}.{nameof(vm.Role)}");
        }
        if (!ModelState.IsValid) return await EditView(stored, vm);

        var update = await _users.UpdateAsync(new UpdateUserInput(id, vm.DisplayName, vm.Email, vm.Role), Me);
        if (!update.Result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, update.Result.Error!);
            return await EditView(stored, vm);
        }
        if (update.ChangedFields.Count == 0)
        {
            TempData["Info"] = "No changes to save.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var after = await _users.GetAsync(id) ?? stored;
        await _audit.LogAsync("UserUpdated", "User", Ref(id), UserAuditSummary.Updated(stored, after, update.ChangedFields, update.RemovedFromJobs));

        // The sidebar shows the name from the cookie, so re-issue it. A name change does not rotate the stamp.
        if (isMe && update.ChangedFields.Contains(UserField.Name))
        {
            var tenantId = _tenant.CurrentTenantId!.Value;
            var session = await _identity.GetSessionAsync(id, tenantId);
            if (session is null) return RedirectToAction("Login", "Account");
            await AtsSignIn.SignInAsync(HttpContext, id, tenantId, session.Role, after.DisplayName, session.SecurityStamp, false);
        }

        var teams = update.RemovedFromJobs > 0 ? $", {UserAuditSummary.RemovedFromTeams(update.RemovedFromJobs)}" : "";
        TempData["Success"] = update.SignedOut
            ? $"{after.DisplayName} updated{teams}. They are signed out."
            : $"{after.DisplayName} updated{teams}.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    // Every outcome redirects back to Edit, so a refresh never re-posts or lands on this action. The
    // error travels in TempData; the password is never put there or in the URL.
    [HttpPost]
    public async Task<IActionResult> ResetPassword(int id, [Bind(Prefix = ResetPrefix)] UserResetPasswordViewModel vm)
    {
        var stored = await _users.GetAsync(id);
        if (stored is null) return NotFound();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => m.Length > 0)
                ?? "Enter a temporary password.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var result = await _users.ResetPasswordAsync(id, vm.TemporaryPassword, Me);
        if (result.Succeeded)
        {
            await _audit.LogAsync("UserPasswordReset", "User", Ref(id), $"Reset password for '{stored.Email}'");
            TempData["Success"] = $"Password reset for {stored.DisplayName}. They are signed out and must set a new one at next sign-in.";
        }
        else TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    public Task<IActionResult> Deactivate(int id) => SetActive(id, false);

    [HttpPost]
    public Task<IActionResult> Reactivate(int id) => SetActive(id, true);

    private async Task<IActionResult> SetActive(int id, bool active)
    {
        var stored = await _users.GetAsync(id);
        if (stored is null) return NotFound();

        var (result, changed) = await _users.SetActiveAsync(id, active, Me);
        var name = stored.DisplayName;
        if (!result.Succeeded) TempData["Error"] = result.Error;
        else if (!changed) TempData["Info"] = active ? $"{name} is already active." : $"{name} is already deactivated.";
        else
        {
            await _audit.LogAsync(active ? "UserReactivated" : "UserDeactivated", "User", Ref(id),
                $"{(active ? "Reactivated" : "Deactivated")} '{stored.Email}'");
            TempData["Success"] = active ? $"{name} reactivated." : $"{name} deactivated and signed out.";
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<ViewResult> EditView(UserListItem stored, UserEditViewModel? details = null)
    {
        var assignedJobs = stored.Role == AtsRole.HiringManager ? await _hiringTeam.JobsForAsync(stored.Id) : null;
        return View(nameof(Edit), new UserEditPageViewModel(stored, stored.Id == Me, details ?? UserEditViewModel.From(stored), new(), assignedJobs));
    }

    private static string Ref(int id) => id.ToString(CultureInfo.InvariantCulture);
}
