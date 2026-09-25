using Ats.Application.Integration;
using Ats.Domain.Entities;
using Xunit;

namespace Ats.Tests.Integration;

public class IntegrationBlockedTests
{
    private static TenantSettings Usable() => new()
    {
        IntegrationEnabled = true,
        ReferralToolCustomerId = 42,
        ReferralToolBaseUrl = "https://api.referraltool.example",
        ReferralToolApiKey = "<API_KEY>",
        ReferralToolAuthToken = "<AUTH_TOKEN>"
    };

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(500)]
    public void Enabled_usable_integration_is_not_blocked(int? latestStatus)
    {
        Assert.Null(ReferralToolRules.BlockedReason(Usable(), latestStatus));
    }

    [Fact]
    public void Missing_settings_row_is_not_blocked()
    {
        Assert.Null(ReferralToolRules.BlockedReason(null, 401));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(401)]
    public void Disabled_integration_stays_silent(int? latestStatus)
    {
        var s = Usable();
        s.IntegrationEnabled = false;
        s.ReferralToolApiKey = null;
        Assert.Null(ReferralToolRules.BlockedReason(s, latestStatus));
    }

    [Fact]
    public void Enabled_integration_with_incomplete_settings_is_blocked_with_the_connection_problem()
    {
        var s = Usable();
        s.ReferralToolAuthToken = null;
        Assert.Equal(ReferralToolRules.ConnectionProblem(s), ReferralToolRules.BlockedReason(s, null));
    }

    [Fact]
    public void Enabled_integration_with_an_invalid_base_url_is_blocked()
    {
        var s = Usable();
        s.ReferralToolBaseUrl = "https://10.0.0.5";
        Assert.Equal(ReferralToolBaseUrl.Validate(s.ReferralToolBaseUrl), ReferralToolRules.BlockedReason(s, 200));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Credentials_rejected_on_the_latest_attempt_block_the_integration(int latestStatus)
    {
        var reason = ReferralToolRules.BlockedReason(Usable(), latestStatus);
        Assert.NotNull(reason);
        Assert.DoesNotContain("<API_KEY>", reason);
        Assert.DoesNotContain("<AUTH_TOKEN>", reason);
    }
}
