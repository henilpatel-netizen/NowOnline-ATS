using System.ComponentModel.DataAnnotations;
using Ats.Application.Common;
using Ats.Application.Users;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public sealed record UsersIndexViewModel(PagedResult<UserListItem> Results, int ActiveCount, string? Q, UserStatusFilter Status, int CurrentUserId);

public static class RoleOptions
{
    public static bool IsAssignable(string? role) => role is not null && AtsRole.Assignable.Contains(role);

    // A role that cannot be assigned here (e.g. HiringManager set in the database) gets an empty leading
    // option, so the browser does not preselect the first role and [Required] blocks a blind save.
    public static IEnumerable<SelectListItem> For(string? selected)
    {
        var options = AtsRole.Assignable.Select(r => new SelectListItem(r, r, r == selected));
        return IsAssignable(selected) ? options : options.Prepend(new SelectListItem("Choose a role", "", true));
    }
}

public class UserCreateViewModel
{
    [Required, StringLength(200), Display(Name = "Name")] public string DisplayName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = AtsRole.Viewer;
    [Required, DataType(DataType.Password), Display(Name = "Temporary password")]
    [StringLength(UserService.MaxPasswordLength)]
    public string TemporaryPassword { get; set; } = "";
}

// The Edit page: the stored record drives the header and the actions; Details and Reset are the two
// posted forms, bound with their own prefixes. A Details error is shown on its field; a Reset error is
// redirected back as a flash message, so the password is never echoed into the page.
public sealed record UserEditPageViewModel(UserListItem User, bool IsMe, UserEditViewModel Details, UserResetPasswordViewModel Reset);

public class UserEditViewModel
{
    [Required, StringLength(200), Display(Name = "Name")] public string DisplayName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = "";

    public static UserEditViewModel From(UserListItem u) => new() { DisplayName = u.DisplayName, Email = u.Email, Role = u.Role };
}

public class UserResetPasswordViewModel
{
    [Required(ErrorMessage = "Enter a temporary password."), DataType(DataType.Password), Display(Name = "Temporary password")]
    [StringLength(UserService.MaxPasswordLength)]
    public string TemporaryPassword { get; set; } = "";
}
