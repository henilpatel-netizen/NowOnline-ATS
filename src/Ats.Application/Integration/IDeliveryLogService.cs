using Ats.Application.Common;
using Ats.Domain.Enums;

namespace Ats.Application.Integration;

public interface IDeliveryLogService
{
    Task<PagedResult<DeliveryLogEntry>> SearchAsync(OutboxStatus? status, int page, int pageSize, CancellationToken ct = default);
}
