using System.ComponentModel.DataAnnotations;
using Ats.Application.Users;

namespace Ats.Web.Models;

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    [StringLength(UserService.MaxPasswordLength)]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "New password")]
    [StringLength(UserService.MaxPasswordLength)]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "Confirm new password")]
    [StringLength(UserService.MaxPasswordLength)]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}
