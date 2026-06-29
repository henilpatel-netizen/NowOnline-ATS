using Ats.Application.Common;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Web.Models;

public class JobsIndexViewModel
{
    public PagedResult<Job> Results { get; set; } = default!;
    public string? Q { get; set; }
    public JobStatus? Status { get; set; }
}
