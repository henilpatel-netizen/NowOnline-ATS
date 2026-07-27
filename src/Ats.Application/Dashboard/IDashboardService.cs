namespace Ats.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummary> GetAsync(CancellationToken ct = default);
}
