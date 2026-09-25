using Ats.Application.Integration;
using Ats.Domain.Entities;
using Xunit;

namespace Ats.Tests.Integration;

public class ReferralToolRulesTests
{
    [Theory]
    [InlineData(200, false)]
    [InlineData(200, true)]
    [InlineData(204, false)]
    public void Status_update_2xx_is_delivered(int status, bool hadPossiblyProcessedAttempt)
    {
        Assert.Equal(OutboxOutcome.Delivered, ReferralToolRules.ClassifyStatusUpdate(true, status, hadPossiblyProcessedAttempt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Status_update_not_reached_is_transient(bool hadPossiblyProcessedAttempt)
    {
        Assert.Equal(OutboxOutcome.Transient, ReferralToolRules.ClassifyStatusUpdate(false, 0, hadPossiblyProcessedAttempt));
    }

    [Theory]
    [InlineData(500, false)]
    [InlineData(503, true)]
    public void Status_update_5xx_is_transient(int status, bool hadPossiblyProcessedAttempt)
    {
        Assert.Equal(OutboxOutcome.Transient, ReferralToolRules.ClassifyStatusUpdate(true, status, hadPossiblyProcessedAttempt));
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(401, true)]
    [InlineData(403, false)]
    [InlineData(403, true)]
    public void Status_update_credential_rejection_is_postponed(int status, bool hadPossiblyProcessedAttempt)
    {
        Assert.Equal(OutboxOutcome.Postpone, ReferralToolRules.ClassifyStatusUpdate(true, status, hadPossiblyProcessedAttempt));
    }

    [Theory]
    [InlineData(408, false)]
    [InlineData(408, true)]
    [InlineData(429, false)]
    [InlineData(429, true)]
    public void Status_update_timeout_and_rate_limit_are_transient(int status, bool hadPossiblyProcessedAttempt)
    {
        Assert.Equal(OutboxOutcome.Transient, ReferralToolRules.ClassifyStatusUpdate(true, status, hadPossiblyProcessedAttempt));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Auth_rejections_are_credential_rejections(int status)
    {
        Assert.True(ReferralToolRules.IsCredentialRejection(status));
    }

    [Theory]
    [InlineData(0)]      // not reached
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    public void Other_statuses_are_not_credential_rejections(int status)
    {
        Assert.False(ReferralToolRules.IsCredentialRejection(status));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    public void Status_update_other_4xx_on_first_attempt_is_failed(int status)
    {
        Assert.Equal(OutboxOutcome.Failed, ReferralToolRules.ClassifyStatusUpdate(true, status, false));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    public void Status_update_other_4xx_after_a_possibly_processed_attempt_is_an_idempotent_duplicate(int status)
    {
        Assert.Equal(OutboxOutcome.Delivered, ReferralToolRules.ClassifyStatusUpdate(true, status, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Status_update_redirect_is_failed(bool hadPossiblyProcessedAttempt)
    {
        Assert.Equal(OutboxOutcome.Failed, ReferralToolRules.ClassifyStatusUpdate(true, 302, hadPossiblyProcessedAttempt));
    }

    [Theory]
    [InlineData(null)]   // unreached, or sent and the worker died before the reply was recorded
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(500)]
    [InlineData(503)]
    public void Earlier_attempt_that_ReferralTool_may_have_recorded_counts(int? status)
    {
        var mayHave = ReferralToolRules.MayHaveBeenProcessed.Compile();
        Assert.True(mayHave(new WebhookDelivery { HttpStatus = status }));
    }

    [Theory]
    [InlineData(0)]      // definitely not sent
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(408)]
    [InlineData(429)]
    public void Earlier_attempt_that_ReferralTool_rejected_does_not_count(int status)
    {
        var mayHave = ReferralToolRules.MayHaveBeenProcessed.Compile();
        Assert.False(mayHave(new WebhookDelivery { HttpStatus = status }));
    }

    public static TheoryData<Exception> NotSentFailures => new()
    {
        new HttpRequestException(HttpRequestError.ConnectionError),
        new HttpRequestException(HttpRequestError.NameResolutionError),
        new HttpRequestException(HttpRequestError.SecureConnectionError),
        new UriFormatException()
    };

    public static TheoryData<Exception> MaybeSentFailures => new()
    {
        new HttpRequestException(HttpRequestError.Unknown),
        new HttpRequestException(HttpRequestError.ResponseEnded),
        new HttpRequestException("no error code"),
        new TaskCanceledException()
    };

    [Theory]
    [MemberData(nameof(NotSentFailures))]
    public void Failures_before_the_request_leaves_are_definitely_not_sent(Exception ex)
    {
        Assert.True(ReferralToolRules.IsDefinitelyNotSent(ex));
    }

    [Theory]
    [MemberData(nameof(MaybeSentFailures))]
    public void Other_transport_failures_may_have_been_sent(Exception ex)
    {
        Assert.False(ReferralToolRules.IsDefinitelyNotSent(ex));
    }

    [Theory]
    [MemberData(nameof(MaybeSentFailures))]
    public void Transport_failures_become_a_call_result(Exception ex)
    {
        Assert.True(ReferralToolRules.IsTransportFailure(ex));
    }

    [Fact]
    public void A_response_body_cut_off_mid_read_is_a_transport_failure()
    {
        Assert.True(ReferralToolRules.IsTransportFailure(new HttpIOException(HttpRequestError.ResponseEnded)));
        Assert.False(ReferralToolRules.IsDefinitelyNotSent(new HttpIOException(HttpRequestError.ResponseEnded)));
    }

    [Fact]
    public void Programming_errors_are_not_transport_failures()
    {
        Assert.False(ReferralToolRules.IsTransportFailure(new InvalidOperationException()));
    }

    [Fact]
    public void Recorded_status_is_the_http_status_when_reached()
    {
        Assert.Equal(400, ReferralToolRules.RecordedStatus(new ReferralCallResult(true, 400, "bad")));
    }

    [Fact]
    public void Recorded_status_is_null_when_the_request_may_have_been_sent()
    {
        Assert.Null(ReferralToolRules.RecordedStatus(new ReferralCallResult(false, 0, "timeout")));
    }

    [Fact]
    public void Recorded_status_is_zero_when_the_request_was_definitely_not_sent()
    {
        Assert.Equal(0, ReferralToolRules.RecordedStatus(new ReferralCallResult(false, 0, "refused", MaybeSent: false)));
    }

    private static OutboxOutcome RetryAfter(ReferralCallResult earlier, int retryStatus)
    {
        var row = new WebhookDelivery { HttpStatus = ReferralToolRules.RecordedStatus(earlier) };
        var possiblyProcessed = ReferralToolRules.MayHaveBeenProcessed.Compile()(row);
        return ReferralToolRules.ClassifyStatusUpdate(true, retryStatus, possiblyProcessed);
    }

    [Fact]
    public void Connection_failure_then_400_is_failed()
    {
        Assert.Equal(OutboxOutcome.Failed, RetryAfter(new ReferralCallResult(false, 0, "refused", MaybeSent: false), 400));
    }

    [Fact]
    public void Timeout_then_400_is_delivered()
    {
        Assert.Equal(OutboxOutcome.Delivered, RetryAfter(new ReferralCallResult(false, 0, "timeout"), 400));
    }

    [Fact]
    public void Connection_check_ignores_the_enabled_switch()
    {
        var s = Usable();
        s.IntegrationEnabled = false;
        Assert.Null(ReferralToolRules.ConnectionProblem(s));
    }

    [Fact]
    public void Connection_check_rejects_incomplete_settings()
    {
        var s = Usable();
        s.ReferralToolAuthToken = null;
        Assert.NotNull(ReferralToolRules.ConnectionProblem(s));
    }

    [Fact]
    public void Connection_check_rejects_a_base_url_that_fails_validation()
    {
        var s = Usable();
        s.ReferralToolBaseUrl = "https://10.0.0.5";
        Assert.Equal(ReferralToolBaseUrl.Validate(s.ReferralToolBaseUrl), ReferralToolRules.ConnectionProblem(s));
    }

    private static TenantSettings Usable() => new()
    {
        IntegrationEnabled = true,
        ReferralToolCustomerId = 42,
        ReferralToolBaseUrl = "https://api.referraltool.example",
        ReferralToolApiKey = "<API_KEY>",
        ReferralToolAuthToken = "<AUTH_TOKEN>"
    };

    [Fact]
    public void Complete_settings_with_a_valid_url_are_usable()
    {
        Assert.Null(ReferralToolRules.SettingsProblem(Usable()));
    }

    [Fact]
    public void Missing_settings_row_is_unusable()
    {
        Assert.NotNull(ReferralToolRules.SettingsProblem(null));
    }

    [Fact]
    public void Disabled_integration_is_unusable()
    {
        var s = Usable();
        s.IntegrationEnabled = false;
        Assert.NotNull(ReferralToolRules.SettingsProblem(s));
    }

    [Fact]
    public void Missing_customer_id_is_unusable()
    {
        var s = Usable();
        s.ReferralToolCustomerId = null;
        Assert.NotNull(ReferralToolRules.SettingsProblem(s));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Blank_base_url_is_unusable(string? value)
    {
        var s = Usable();
        s.ReferralToolBaseUrl = value;
        Assert.NotNull(ReferralToolRules.SettingsProblem(s));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Blank_api_key_is_unusable(string? value)
    {
        var s = Usable();
        s.ReferralToolApiKey = value;
        Assert.NotNull(ReferralToolRules.SettingsProblem(s));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Blank_auth_token_is_unusable(string? value)
    {
        var s = Usable();
        s.ReferralToolAuthToken = value;
        Assert.NotNull(ReferralToolRules.SettingsProblem(s));
    }

    [Theory]
    [InlineData("http://api.referraltool.example")]
    [InlineData("https://localhost")]
    [InlineData("https://169.254.169.254")]
    public void Base_url_that_fails_validation_is_unusable(string url)
    {
        var s = Usable();
        s.ReferralToolBaseUrl = url;
        Assert.Equal(ReferralToolBaseUrl.Validate(url), ReferralToolRules.SettingsProblem(s));
    }

    [Theory]
    [InlineData("{\"exists\":true}", true)]
    [InlineData("{\"exists\":false}", false)]
    [InlineData("{\"exists\":false,\"extra\":1}", false)]
    public void Vacancy_check_reply_with_an_exists_flag_is_read(string body, bool expected)
    {
        Assert.Equal(expected, ReferralToolRules.ReadVacancyExists(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("{\"exists\":")]
    [InlineData("{}")]
    [InlineData("{\"exists\":\"true\"}")]
    [InlineData("true")]
    [InlineData("[]")]
    public void Vacancy_check_reply_without_a_readable_exists_flag_is_null(string body)
    {
        Assert.Null(ReferralToolRules.ReadVacancyExists(body));
    }
}
