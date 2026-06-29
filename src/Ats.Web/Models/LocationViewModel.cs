using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class LocationViewModel
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [StringLength(120)] public string? City { get; set; }
}
