using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class DepartmentViewModel
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
}
