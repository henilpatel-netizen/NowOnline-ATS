namespace Ats.Application.Shell;

public interface IShellSummaryService
{
    Task<ShellSummary> GetAsync(CancellationToken ct = default);
}
