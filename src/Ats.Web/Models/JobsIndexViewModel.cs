using Ats.Application.Common;
using Ats.Application.Jobs;
using Ats.Domain.Enums;

namespace Ats.Web.Models;

public class JobsIndexViewModel
{
    public PagedResult<JobListItem> Results { get; set; } = default!;
    public string? Q { get; set; }
    public JobStatus? Status { get; set; }
}
