using Ats.Application.Abstractions;

namespace Ats.Tests.Fakes;

// Hashing only: "hash:" + password, so a test can read back which password was set.
public sealed class FakeIdentityService : IIdentityService
{
    public string HashPassword(string password) => "hash:" + password;
    public bool VerifyPassword(string hash, string password) => hash == "hash:" + password;
    public Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<UserSession?> GetSessionAsync(int userId, int tenantId, CancellationToken ct = default) => throw new NotSupportedException();
}
