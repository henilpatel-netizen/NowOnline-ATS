using Ats.Application.Abstractions;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignInResult = Ats.Application.Abstractions.SignInResult;

namespace Ats.Infrastructure.Identity;

public sealed class IdentityService : IIdentityService
{
    private readonly AtsDbContext _db;
    private readonly PasswordHasher<AppUser> _hasher = new();

    public IdentityService(AtsDbContext db) => _db = db;

    public string HashPassword(string password) => _hasher.HashPassword(new AppUser(), password);

    public bool VerifyPassword(string hash, string password) =>
        _hasher.VerifyHashedPassword(new AppUser(), hash, password) != PasswordVerificationResult.Failed;

    public async Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        // IgnoreQueryFilters: sign-in happens before a tenant is in context. Email is globally unique
        // (IX_Users_Email), so this resolves to at most one user, in exactly one tenant.
        var user = await _db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null || !VerifyPassword(user.PasswordHash, password))
            return new SignInResult(false, null, null, null, null, "Invalid email or password.");

        if (!user.IsActive)
            return new SignInResult(false, null, null, null, null, "This account is not available. Contact your administrator.");

        // A suspended tenant must not be able to sign in (the career-site middleware already 404s
        // suspended slugs; the back-office login is gated here).
        var tenantActive = await _db.Tenants
            .AnyAsync(t => t.Id == user.TenantId && t.Status == TenantStatus.Active, ct);
        if (!tenantActive)
            return new SignInResult(false, null, null, null, null, "This account is not available. Contact your administrator.");

        return new SignInResult(true, user.Id, user.TenantId, user.Role, user.DisplayName, null,
            user.SecurityStamp, user.MustChangePassword);
    }

    public async Task<UserSession?> GetSessionAsync(int userId, int tenantId, CancellationToken ct = default)
    {
        // IgnoreQueryFilters: this runs while the cookie is being validated, before HttpContext.User (and so
        // the tenant context) exists. It is scoped explicitly by the tenant_id from the same cookie.
        var row = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId && u.TenantId == tenantId)
            .Select(u => new
            {
                IsActive = u.IsActive && _db.Tenants.Any(t => t.Id == u.TenantId && t.Status == TenantStatus.Active),
                u.SecurityStamp,
                u.Role
            })
            .SingleOrDefaultAsync(ct);
        return row is null ? null : new UserSession(row.IsActive, row.SecurityStamp, row.Role);
    }
}
