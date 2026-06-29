using Ats.Application.Common;
using Ats.Application.Integration;
using Ats.Domain.Enums;

namespace Ats.Web.Models;

public class DeliveryLogViewModel
{
    public PagedResult<DeliveryLogEntry> Results { get; set; } = default!;
    public OutboxStatus? Status { get; set; }
}
