using Ats.Application.Common;
using Ats.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public class AuditIndexViewModel
{
    public PagedResult<AuditEntry> Results { get; set; } = default!;
    public string? Q { get; set; }
    public string? Action { get; set; }
    public string? Range { get; set; }   // "7" | "30" | null (all)
    public List<SelectListItem> Actions { get; set; } = new();
}
