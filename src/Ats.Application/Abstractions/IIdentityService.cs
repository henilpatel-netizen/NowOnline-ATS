namespace Ats.Application.Abstractions;

public record SignInResult(bool Succeeded, int? UserId, int? TenantId, string? Role, string? DisplayName, string? Error,
    Guid? SecurityStamp = null, bool MustChangePassword = false);

// What the cookie is checked against on each request. IsActive is false when the user is deactivated
// or the tenant is suspended.
public sealed record UserSession(bool IsActive, Guid SecurityStamp, string Role);

public interface IIdentityService
{
    Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);
    Task<UserSession?> GetSessionAsync(int userId, int tenantId, CancellationToken ct = default);
    string HashPassword(string password);
    bool VerifyPassword(string hash, string password);
}
