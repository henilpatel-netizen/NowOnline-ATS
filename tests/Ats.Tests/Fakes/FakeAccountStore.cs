using Ats.Application.Abstractions;
using Ats.Application.Accounts;
using Ats.Domain.Entities;

namespace Ats.Tests.Fakes;

public sealed class FakeAccountStore : IAccountStore
{
    public List<AppUser> Users { get; } = new();
    public HashSet<int> SuspendedTenants { get; } = new();
    public List<(AppUser User, string Action, string Summary)> Audits { get; } = new();
    public HashSet<string> TakenEmails { get; } = new();
    public bool RejectNextSaveAsDuplicate { get; set; }
    public int SaveCount { get; private set; }

    public Task<AppUser?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task<AppUser?> GetAsync(int tenantId, int userId, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.TenantId == tenantId && u.Id == userId));

    public Task<bool> TenantActiveAsync(int tenantId, CancellationToken ct = default) =>
        Task.FromResult(!SuspendedTenants.Contains(tenantId));

    public Task<string> TenantNameAsync(int tenantId, CancellationToken ct = default) => Task.FromResult("Acme");

    public Task<bool> EmailTakenAsync(string normalisedEmail, CancellationToken ct = default) =>
        Task.FromResult(TakenEmails.Contains(normalisedEmail));

    public void Audit(AppUser user, string action, string summary) => Audits.Add((user, action, summary));

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<bool> TrySaveAsync(CancellationToken ct = default)
    {
        if (RejectNextSaveAsDuplicate)
        {
            RejectNextSaveAsDuplicate = false;
            return Task.FromResult(false);
        }
        SaveCount++;
        return Task.FromResult(true);
    }
}

// Readable on purpose: the real signature and expiry checks are covered by AccountTokensTests.
public sealed class FakeTokens : IAccountTokens
{
    public string Create(AccountTokenPurpose purpose, AccountToken token) =>
        $"{purpose}|{token.UserId}|{token.TenantId}|{token.SecurityStamp}|{token.NewEmail}";

    public AccountToken? Read(AccountTokenPurpose purpose, string? token)
    {
        var parts = token?.Split('|');
        return parts is { Length: 5 } && parts[0] == purpose.ToString()
            ? new AccountToken(int.Parse(parts[1]), int.Parse(parts[2]), Guid.Parse(parts[3]), parts[4].Length == 0 ? null : parts[4])
            : null;
    }
}
