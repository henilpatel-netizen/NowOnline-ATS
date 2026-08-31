namespace Ats.Application.Common;

// Cross-cutting result for a command that either succeeded or failed with a message. Lives in Common
// beside PagedResult: it previously sat in the Departments feature namespace, which made every other
// service take a dependency on Departments just to return a result.
public record OperationResult(bool Succeeded, string? Error)
{
    public static readonly OperationResult Ok = new(true, null);
    public static OperationResult Fail(string error) => new(false, error);
}
