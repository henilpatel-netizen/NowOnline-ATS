using System.Globalization;
using Ats.Application.Abstractions;
using Ats.Application.Accounts;
using Ats.Application.Auditing;
using Ats.Application.Common;
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

    private const int PageSize = 20;

    private readonly IUserService _users;
    private readonly IUserListQuery _userList;
    private readonly IIdentityService _identity;
    private readonly ICurrentUser _current;
    private readonly ITenantContext _tenant;
    private readonly IHiringTeamQuery _hiringTeam;
    private readonly IAuditLogger _audit;
    private readonly IInvitationService _invites;
    private readonly IAccountRecoveryService _recovery;
    private readonly IEmailChangeService _emailChange;

    public UsersController(IUserService users, IUserListQuery userList, IIdentityService identity, ICurrentUser current, ITenantContext tenant,
        IHiringTeamQuery hiringTeam, IAuditLogger audit, IInvitationService invites, IAccountRecoveryService recovery,
        IEmailChangeService emailChange)
    {
        _users = users; _userList = userList; _identity = identity; _current = current; _tenant = tenant; _hiringTeam = hiringTeam; _audit = audit;
        _invites = invites; _recovery = recovery; _emailChange = emailChange;
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
        var (result, userId) = await _users.CreateAsync(new CreateUserInput(vm.DisplayName, vm.Email, vm.Role));
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(vm);
        }
        var id = userId!.Value;
        var email = EmailAddress.Normalise(vm.Email);
        await _audit.LogAsync("UserCreated", "User", Ref(id), $"Invited '{email}' as {vm.Role}");

        // A second save: when it fails the user still exists, invite-pending, and the Owner can resend.
        var invite = await _invites.SendInviteAsync(id);
        if (!invite.Succeeded)
        {
            TempData["Error"] = $"{vm.DisplayName.Trim()} was added, but the invitation could not be sent. Use Resend invite to try again.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        TempData["Success"] = $"Invitation sent to {email}.";
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
        if (update.ChangedFields.Count == 0 && update.PendingEmail is null)
        {
            TempData["Info"] = "No changes to save.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var after = await _users.GetAsync(id) ?? stored;
        if (update.ChangedFields.Count > 0)
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
        var updated = update.ChangedFields.Count == 0 ? ""
            : update.SignedOut && !stored.InvitePending ? $"{after.DisplayName} updated{teams}. They are signed out. "
            : $"{after.DisplayName} updated{teams}. ";

        // Second saves, after the update: a failure here leaves the update in place and says what did not happen.
        if (update.PendingEmail is { } newEmail)
        {
            var request = await _emailChange.RequestAsync(id, newEmail, Me);
            if (!request.Succeeded)
            {
                TempData["Error"] = $"{updated}The email address was not changed: {request.Error}";
                return RedirectToAction(nameof(Edit), new { id });
            }
            await _audit.LogAsync("UserEmailChangeRequested", "User", Ref(id), $"Requested an email change from '{after.Email}' to '{newEmail}'");
            // The request rotates the stamp; a role change in the same save has said so already.
            var signedOut = update.SignedOut ? "" : "They are signed out. ";
            TempData["Success"] = $"{updated}{signedOut}Verification sent to {newEmail}. The address changes when they confirm.";
        }
        else if (stored.InvitePending && update.ChangedFields.Contains(UserField.Email))
        {
            var invite = await _invites.SendInviteAsync(id);
            if (invite.Succeeded) TempData["Success"] = $"{updated}Invitation sent to {after.Email}.";
            else TempData["Error"] = $"{updated}The invitation could not be sent to {after.Email}: {invite.Error}";
        }
        else TempData["Success"] = updated.TrimEnd();
        return RedirectToAction(nameof(Edit), new { id });
    }

    // Sends the same link as Forgot password; the password and the user's sessions are left alone.
    [HttpPost]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var stored = await _users.GetAsync(id);
        if (stored is null) return NotFound();

        var result = await _recovery.SendResetLinkAsync(id, Me);
        if (result.Succeeded)
        {
            await _audit.LogAsync("UserPasswordResetSent", "User", Ref(id), $"Sent a password reset link to '{stored.Email}'");
            TempData["Success"] = $"Reset link sent to {stored.Email}.";
        }
        else TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ResendInvite(int id)
    {
        var stored = await _users.GetAsync(id);
        if (stored is null) return NotFound();

        var result = await _invites.ResendAsync(id);
        if (result.Succeeded)
        {
            await _audit.LogAsync("UserInviteResent", "User", Ref(id), $"Resent the invitation to '{stored.Email}'");
            TempData["Success"] = $"Invitation sent again to {stored.Email}.";
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
        return View(nameof(Edit), new UserEditPageViewModel(stored, stored.Id == Me, details ?? UserEditViewModel.From(stored), assignedJobs));
    }

    private static string Ref(int id) => id.ToString(CultureInfo.InvariantCulture);
}
