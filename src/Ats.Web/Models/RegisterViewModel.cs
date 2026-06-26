using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class RegisterViewModel
{
    [Required] public string CompanyName { get; set; } = "";
    [Required] public string Slug { get; set; } = "";
    [Required] public string OwnerName { get; set; } = "";
    [Required, EmailAddress] public string OwnerEmail { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
}
