using System.ComponentModel.DataAnnotations;
using Ats.Application.Common;
using Ats.Application.Jobs;
using Ats.Application.Users;
using Ats.Domain.Enums;
using Ats.Web.Models.Shared;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public sealed record UsersIndexViewModel(PagedResult<UserListItem> Results, int ActiveCount, string? Q, UserStatusFilter Status, int CurrentUserId);

public static class RoleOptions
{
    public static bool IsAssignable(string? role) => role is not null && AtsRole.Assignable.Contains(role);

    public static string Label(string role) => role == AtsRole.HiringManager ? "Hiring manager" : role;

    // A role that cannot be assigned here (a value set in the database) gets an empty leading option, so
    // the browser does not preselect the first role and [Required] blocks a blind save.
    public static IEnumerable<SelectListItem> For(string? selected)
    {
        var options = AtsRole.Assignable.Select(r => new SelectListItem(Label(r), r, r == selected));
        return IsAssignable(selected) ? options : options.Prepend(new SelectListItem("Choose a role", "", true));
    }
}

public static class UserStatus
{
    public static StatusPillModel Pill(UserListItem u) =>
        !u.IsActive ? new("Deactivated", PillTone.Neutral)
        : u.InvitePending ? new("Invited", PillTone.Info)
        : new("Active", PillTone.Success);
}

public class UserCreateViewModel
{
    [Required, StringLength(200), Display(Name = "Name")] public string DisplayName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = AtsRole.Viewer;
}

// The Edit page: the stored record drives the header and the actions; Details is the posted form, bound
// with its own prefix. AssignedJobs is null unless the user is a hiring manager.
public sealed record UserEditPageViewModel(UserListItem User, bool IsMe, UserEditViewModel Details,
    IReadOnlyList<AssignedJob>? AssignedJobs);

public class UserEditViewModel
{
    [Required, StringLength(200), Display(Name = "Name")] public string DisplayName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = "";

    public static UserEditViewModel From(UserListItem u) => new() { DisplayName = u.DisplayName, Email = u.Email, Role = u.Role };
}
