namespace Ats.Application.Abstractions;

public enum AccountTokenPurpose { PasswordReset, Invite, EmailChange }

public sealed record AccountToken(int UserId, int TenantId, Guid SecurityStamp, string? NewEmail = null);

// Stateless account links. A token carries the user's SecurityStamp, so rotating the stamp invalidates it.
public interface IAccountTokens
{
    // Lifetime follows the purpose: password reset 1 hour, invite 7 days, email change 24 hours.
    string Create(AccountTokenPurpose purpose, AccountToken token);

    // Null when the token is null/empty, tampered, expired or made for another purpose. Never throws.
    AccountToken? Read(AccountTokenPurpose purpose, string? token);
}
