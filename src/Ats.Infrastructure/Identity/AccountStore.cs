using System.Globalization;
using Ats.Application.Accounts;
using Ats.Application.Common;
using Ats.Application.Tenancy;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Ats.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Identity;

// Documented filter bypass: every user read is scoped by the globally unique email, or by an explicit
// tenant id AND user id from a validated account token. Tenants is not tenant-filtered.
public sealed class AccountStore : IAccountStore
{
    private readonly AtsDbContext _db;
    private readonly IOnboardingStore _onboarding;

    public AccountStore(AtsDbContext db, IOnboardingStore onboarding)
    {
        _db = db;
        _onboarding = onboarding;
    }

    public Task<AppUser?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalised = EmailAddress.Normalise(email);
        return _db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == normalised, ct);
    }

    public Task<AppUser?> GetAsync(int tenantId, int userId, CancellationToken ct = default) =>
        _db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);

    public Task<bool> TenantActiveAsync(int tenantId, CancellationToken ct = default) =>
        _db.Tenants.AnyAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, ct);

    public async Task<string> TenantNameAsync(int tenantId, CancellationToken ct = default) =>
        await _db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).SingleOrDefaultAsync(ct) ?? string.Empty;

    public Task<bool> EmailTakenAsync(string normalisedEmail, CancellationToken ct = default) =>
        _onboarding.EmailExistsAsync(normalisedEmail, ct);

    public void Audit(AppUser user, string action, string summary) =>
        // A non-zero TenantId is left alone by TenantSaveChangesInterceptor; there is no tenant in context here.
        _db.AuditEntries.Add(new AuditEntry
        {
            TenantId = user.TenantId,
            Action = action,
            EntityType = "User",
            EntityRef = user.Id.ToString(CultureInfo.InvariantCulture),
            Summary = summary,
            UserId = user.Id,
            UserName = user.DisplayName,
            OccurredAt = DateTimeOffset.UtcNow
        });

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task<bool> TrySaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (UserRepository.IsDuplicateEmail(ex))
        {
            // Nothing from the rejected save (the user change, its audit entry) may go out with a later one.
            _db.ChangeTracker.Clear();
            return false;
        }
    }
}
