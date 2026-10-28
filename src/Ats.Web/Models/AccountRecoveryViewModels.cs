using System.ComponentModel.DataAnnotations;
using Ats.Application.Accounts;
using Ats.Application.Users;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ats.Web.Models;

public class ForgotPasswordViewModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    // Set after the POST; the same for every address, known or not.
    [BindNever] public bool Sent { get; set; }
}

public class ResetPasswordViewModel
{
    public string Token { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "New password")]
    [StringLength(UserService.MaxPasswordLength)]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "Confirm new password")]
    [StringLength(UserService.MaxPasswordLength)]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";

    [BindNever] public bool LinkUsable { get; set; } = true;
}

public class AcceptInviteViewModel
{
    public string Token { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "Password")]
    [StringLength(UserService.MaxPasswordLength)]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "Confirm password")]
    [StringLength(UserService.MaxPasswordLength)]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";

    // Null when the link cannot be used; read from the token on every render, never posted.
    [BindNever] public InviteDetails? Invite { get; set; }
}

public class ConfirmEmailViewModel
{
    public string Token { get; set; } = "";

    // Null when the link cannot be used; read from the token on every render, never posted.
    [BindNever] public string? NewEmail { get; set; }
}
