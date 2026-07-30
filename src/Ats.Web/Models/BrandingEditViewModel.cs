using System.ComponentModel.DataAnnotations;
using Ats.Domain.Enums;

namespace Ats.Web.Models;

public class BrandingEditViewModel
{
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Use a 6-digit hex colour like #0085CA.")]
    public string? AccentColor { get; set; }
    public SidebarTheme SidebarTheme { get; set; } = SidebarTheme.Dark;
    [StringLength(160)] public string? CareerHeroHeadline { get; set; }
    [StringLength(160)] public string? CareerHeroHeadlineOutlined { get; set; }
    [StringLength(600)] public string? CareerHeroIntro { get; set; }

    public string TenantName { get; set; } = "";
    public string TenantSlug { get; set; } = "";
}
