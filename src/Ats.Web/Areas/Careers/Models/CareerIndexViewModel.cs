using Ats.Application.Branding;
using Ats.Domain.Entities;

namespace Ats.Web.Areas.Careers.Models;

public class CareerIndexViewModel
{
    public TenantBranding Branding { get; set; } = default!;
    public string Slug { get; set; } = "";
    public IReadOnlyList<Job> Jobs { get; set; } = new List<Job>();
}
