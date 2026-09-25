using Ats.Application.Integration;
using Xunit;

namespace Ats.Tests.Integration;

public class ReferralToolBaseUrlTests
{
    [Theory]
    [InlineData("https://api.referraltool.nl")]
    [InlineData("https://api.referraltool.nl/")]
    [InlineData("https://api.referraltool.nl:8443/base")]
    [InlineData("HTTPS://API.REFERRALTOOL.NL")]
    [InlineData("https://8.8.8.8")]
    [InlineData("https://172.32.0.1")]           // just outside 172.16/12
    [InlineData("https://[2001:db8::1]")]
    [InlineData("https://notlocalhost.example")]
    public void Accepts_public_https_urls(string url)
    {
        Assert.Null(ReferralToolBaseUrl.Validate(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_means_not_configured_and_is_allowed(string? url)
    {
        Assert.Null(ReferralToolBaseUrl.Validate(url));
    }

    [Theory]
    [InlineData("http://api.referraltool.nl")]
    [InlineData("ftp://api.referraltool.nl")]
    [InlineData("api.referraltool.nl")]
    [InlineData("//api.referraltool.nl")]
    [InlineData("not a url")]
    public void Rejects_non_https_or_malformed(string url)
    {
        Assert.NotNull(ReferralToolBaseUrl.Validate(url));
    }

    [Theory]
    [InlineData("https://localhost")]
    [InlineData("https://LOCALHOST:5001")]
    [InlineData("https://localhost.")]
    [InlineData("https://api.localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://127.8.9.10")]
    [InlineData("https://2130706433")]           // decimal form of 127.0.0.1
    [InlineData("https://0x7f.0.0.1")]           // hex form of 127.0.0.1
    [InlineData("https://10.0.0.5")]
    [InlineData("https://172.16.0.1")]
    [InlineData("https://172.31.255.255")]
    [InlineData("https://192.168.1.1")]
    [InlineData("https://169.254.169.254")]      // cloud metadata endpoint
    [InlineData("https://0.0.0.0")]
    [InlineData("https://[::1]")]
    [InlineData("https://[::]")]
    [InlineData("https://[fe80::1]")]
    [InlineData("https://[fc00::1]")]
    [InlineData("https://[fd12:3456::1]")]
    [InlineData("https://[::ffff:127.0.0.1]")]
    [InlineData("https://[::ffff:10.0.0.1]")]
    [InlineData("https://[::ffff:169.254.169.254]")]
    public void Rejects_private_loopback_and_link_local_hosts(string url)
    {
        Assert.NotNull(ReferralToolBaseUrl.Validate(url));
    }
}
