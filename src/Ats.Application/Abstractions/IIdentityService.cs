namespace Ats.Application.Abstractions;

public record SignInResult(bool Succeeded, int? UserId, int? TenantId, string? Role, string? Error);

public interface IIdentityService
{
    Task<int> CreateUserAsync(int tenantId, string email, string displayName, string password, string role, CancellationToken ct = default);
    Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);
    string HashPassword(string password);
    bool VerifyPassword(string hash, string password);
}
