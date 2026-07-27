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

    public async Task<int> CreateUserAsync(int tenantId, string email, string displayName, string password, string role, CancellationToken ct = default)
    {
        var user = new AppUser
        {
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            PasswordHash = HashPassword(password),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user.Id;
    }

    public async Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        // IgnoreQueryFilters: sign-in happens before a tenant is in context
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null || !VerifyPassword(user.PasswordHash, password))
            return new SignInResult(false, null, null, null, null, "Invalid email or password.");

        // A suspended tenant must not be able to sign in (the career-site middleware already 404s
        // suspended slugs; the back-office login is gated here).
        var tenantActive = await _db.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.Id == user.TenantId && t.Status == TenantStatus.Active, ct);
        if (!tenantActive)
            return new SignInResult(false, null, null, null, null, "This account is not available. Contact your administrator.");

        return new SignInResult(true, user.Id, user.TenantId, user.Role, user.DisplayName, null);
    }
}
