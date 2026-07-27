using Ats.Domain.Entities;

namespace Ats.Web.Areas.Careers.Models;

public class CareerJobDetailViewModel
{
    public Job Job { get; set; } = default!;
    public string Slug { get; set; } = "";
    public string CodeParamName { get; set; } = "ref";
    public string? Code { get; set; }

    // Preserved on a validation re-display.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Error { get; set; }
}
