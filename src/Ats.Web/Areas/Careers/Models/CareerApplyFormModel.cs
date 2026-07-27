using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Areas.Careers.Models;

public class CareerApplyFormModel
{
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [StringLength(40)] public string? Phone { get; set; }
    [StringLength(36)] public string? SourceCode { get; set; }
}
