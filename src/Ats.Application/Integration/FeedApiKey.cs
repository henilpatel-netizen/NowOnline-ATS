using System.Security.Cryptography;
using System.Text;

namespace Ats.Application.Integration;

public static class FeedApiKey
{
    // The plaintext key shown to the user once (Plan C). URL-safe, high entropy.
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // Only this hash is stored (TenantSettings.FeedApiKeyHash).
    public static string Hash(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(bytes);
    }

    // Constant-time comparison of a presented key against a stored hash.
    public static bool Verify(string presentedKey, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;
        var computed = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
        byte[] stored;
        try { stored = Convert.FromBase64String(storedHash); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }
}
