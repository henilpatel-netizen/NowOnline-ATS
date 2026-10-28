using System.Security.Cryptography;
using System.Text.Json;
using Ats.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Ats.Web.Identity;

public sealed class DataProtectionAccountTokens : IAccountTokens
{
    // The expiry is in the payload and checked against TimeProvider, because the time-limited protector
    // reads the system clock and so cannot be tested.
    private sealed record Payload(AccountToken Token, DateTimeOffset ExpiresAt);

    private readonly IDataProtectionProvider _provider;
    private readonly TimeProvider _time;

    public DataProtectionAccountTokens(IDataProtectionProvider provider, TimeProvider time)
    {
        _provider = provider;
        _time = time;
    }

    public string Create(AccountTokenPurpose purpose, AccountToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var payload = new Payload(token, _time.GetUtcNow() + Lifetime(purpose));
        return WebEncoders.Base64UrlEncode(Protector(purpose).Protect(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }

    public AccountToken? Read(AccountTokenPurpose purpose, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(Protector(purpose).Unprotect(WebEncoders.Base64UrlDecode(token)));
            return payload?.Token is not null && payload.ExpiresAt > _time.GetUtcNow() ? payload.Token : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static TimeSpan Lifetime(AccountTokenPurpose purpose) => purpose switch
    {
        AccountTokenPurpose.PasswordReset => TimeSpan.FromHours(1),
        AccountTokenPurpose.Invite => TimeSpan.FromDays(7),
        AccountTokenPurpose.EmailChange => TimeSpan.FromHours(24),
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    private IDataProtector Protector(AccountTokenPurpose purpose) =>
        _provider.CreateProtector("Ats.AccountTokens", purpose.ToString());
}
