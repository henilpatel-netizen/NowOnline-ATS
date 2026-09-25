using System.Net;
using System.Net.Sockets;

namespace Ats.Application.Integration;

// The API key and auth token are sent to this URL, so it must be https and must not point inside
// the network (SEC-6).
public static class ReferralToolBaseUrl
{
    // Returns a user-facing error, or null when the value is acceptable. Blank means "not configured".
    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return "The ReferralTool base URL must be a full https:// address.";

        return IsPrivateHost(uri)
            ? "The ReferralTool base URL must not point to localhost or a private or link-local network address."
            : null;
    }

    // Literal host check only, no DNS resolution: a public name resolving to a private IP passes.
    private static bool IsPrivateHost(Uri uri)
    {
        if (uri.HostNameType == UriHostNameType.Dns)
        {
            var host = uri.Host.TrimEnd('.');
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        }

        if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] is 0 or 10 or 127
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        return IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6Any)
            || ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
    }
}
