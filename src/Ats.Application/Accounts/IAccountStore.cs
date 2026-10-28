using Ats.Domain.Entities;

namespace Ats.Application.Accounts;

// The sixth documented filter-bypass spot (.claude/rules/multi-tenancy.md). Account links are opened
// signed out, so there is no tenant in context. Callers pass only ids from a validated IAccountTokens
// token, or the globally unique email (like IIdentityService.ValidateCredentialsAsync). Only the
// account-link services in Ats.Application/Accounts may inject it, and they must compare the token's
// SecurityStamp with the loaded user's and check TenantActiveAsync before acting.
public interface IAccountStore
{
    // Tracked, so a caller can change the user and call SaveChangesAsync. Trims and lower-cases the email.
    Task<AppUser?> FindByEmailAsync(string email, CancellationToken ct = default);

    Task<AppUser?> GetAsync(int tenantId, int userId, CancellationToken ct = default);

    Task<bool> TenantActiveAsync(int tenantId, CancellationToken ct = default);

    Task<string> TenantNameAsync(int tenantId, CancellationToken ct = default);

    Task<bool> EmailTakenAsync(string normalisedEmail, CancellationToken ct = default);

    // An entry about the user, by the user, in the user's tenant (TenantId stamped explicitly); saved by
    // the next SaveChangesAsync.
    void Audit(AppUser user, string action, string summary);

    Task SaveChangesAsync(CancellationToken ct = default);

    // False when the save hit the IX_Users_Email unique index (another account took the address first).
    Task<bool> TrySaveAsync(CancellationToken ct = default);
}
