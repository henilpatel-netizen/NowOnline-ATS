using Ats.Application.Email;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Xunit;

namespace Ats.Tests.Email;

public class EmailRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(550, true, null, EmailSendOutcome.Permanent)]
    [InlineData(553, true, null, EmailSendOutcome.Permanent)]
    [InlineData(550, true, "5.1.1", EmailSendOutcome.Permanent)]
    [InlineData(550, true, "5.4.5", EmailSendOutcome.Transient)]
    [InlineData(550, false, null, EmailSendOutcome.Transient)]
    [InlineData(552, false, null, EmailSendOutcome.Transient)]
    [InlineData(554, false, null, EmailSendOutcome.Transient)]
    [InlineData(530, false, null, EmailSendOutcome.Transient)]
    [InlineData(421, false, null, EmailSendOutcome.Transient)]
    [InlineData(452, false, null, EmailSendOutcome.Transient)]
    [InlineData(450, true, null, EmailSendOutcome.Transient)]
    public void Classify(int status, bool recipientRejected, string? enhancedStatus, EmailSendOutcome expected) =>
        Assert.Equal(expected, EmailRules.Classify(status, recipientRejected, enhancedStatus));

    [Theory]
    [InlineData("5.4.5 Daily user sending limit exceeded.", "5.4.5")]
    [InlineData("5.1.1 The email account that you tried to reach does not exist.", "5.1.1")]
    [InlineData("4.7.0 Try again later, closing connection.", "4.7.0")]
    [InlineData("Requested action not taken: mailbox unavailable", null)]
    [InlineData("version 1.2.3 is not a status", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void EnhancedStatus(string? serverText, string? expected) =>
        Assert.Equal(expected, EmailRules.EnhancedStatus(serverText));

    [Theory]
    [InlineData(91, true)]
    [InlineData(90, true)]
    [InlineData(89, false)]
    [InlineData(-5, false)]
    public void HasLeaseFor_requires_room_for_a_worst_case_send(int secondsLeft, bool expected) =>
        Assert.Equal(expected, EmailRules.HasLeaseFor(Now.AddSeconds(secondsLeft), Now));

    [Theory]
    [InlineData("<p>x</p>", null, true)]
    [InlineData(null, "x", true)]
    [InlineData(null, null, false)]
    public void HasBody(string? html, string? text, bool expected) =>
        Assert.Equal(expected, EmailRules.HasBody(html, text));

    [Fact]
    public void Route_without_redirect_keeps_recipient_and_subject() =>
        Assert.Equal(("a@x.nl", "Hi"), EmailRules.Route("a@x.nl", "Hi", null));

    [Fact]
    public void Route_with_redirect_sends_to_redirect_and_names_original() =>
        Assert.Equal(("dev@x.nl", "[to: a@x.nl] Hi"), EmailRules.Route("a@x.nl", "Hi", "dev@x.nl"));

    [Fact]
    public void Route_treats_blank_redirect_as_unset() =>
        Assert.Equal(("a@x.nl", "Hi"), EmailRules.Route("a@x.nl", "Hi", " "));

    [Fact]
    public void BackoffSeconds_doubles_from_base()
    {
        var o = new EmailOptions { BaseBackoffSeconds = 30, MaxBackoffSeconds = 3600 };
        Assert.Equal(60, EmailRules.BackoffSeconds(1, o));
        Assert.Equal(120, EmailRules.BackoffSeconds(2, o));
    }

    [Fact]
    public void BackoffSeconds_caps_at_max() =>
        Assert.Equal(3600, EmailRules.BackoffSeconds(20, new EmailOptions { BaseBackoffSeconds = 30, MaxBackoffSeconds = 3600 }));

    [Fact]
    public void Settle_sent_delivers_and_clears_bodies()
    {
        var m = Message();
        EmailRules.Settle(m, EmailSendOutcome.Sent, null, Now, new EmailOptions());
        Assert.Equal(OutboxStatus.Delivered, m.Status);
        Assert.Null(m.HtmlBody);
        Assert.Null(m.TextBody);
        Assert.Null(m.LastError);
    }

    [Fact]
    public void Settle_permanent_fails_at_once_and_clears_bodies()
    {
        var m = Message();
        EmailRules.Settle(m, EmailSendOutcome.Permanent, "SMTP 550", Now, new EmailOptions());
        Assert.Equal(OutboxStatus.Failed, m.Status);
        Assert.Equal(0, m.Attempts);
        Assert.Null(m.HtmlBody);
        Assert.Null(m.TextBody);
        Assert.Equal("SMTP 550", m.LastError);
    }

    [Fact]
    public void Settle_transient_retries_with_backoff_and_keeps_bodies()
    {
        var m = Message();
        var o = new EmailOptions { BaseBackoffSeconds = 30, MaxBackoffSeconds = 3600, MaxAttempts = 8 };
        EmailRules.Settle(m, EmailSendOutcome.Transient, "SMTP 421", Now, o);
        Assert.Equal(OutboxStatus.Pending, m.Status);
        Assert.Equal(1, m.Attempts);
        Assert.Equal(Now.AddSeconds(60), m.NextAttemptAt);
        Assert.NotNull(m.HtmlBody);
        Assert.NotNull(m.TextBody);
    }

    [Fact]
    public void Settle_transient_at_max_attempts_fails_and_clears_bodies()
    {
        var m = Message();
        m.Attempts = 7;
        EmailRules.Settle(m, EmailSendOutcome.Transient, "SMTP 421", Now, new EmailOptions { MaxAttempts = 8 });
        Assert.Equal(OutboxStatus.Failed, m.Status);
        Assert.Equal(8, m.Attempts);
        Assert.Null(m.HtmlBody);
        Assert.Null(m.TextBody);
    }

    [Fact]
    public void Settle_truncates_error_to_1000() =>
        Assert.Equal(1000, Settled(new string('e', 1500)).LastError!.Length);

    private static EmailMessage Settled(string error)
    {
        var m = Message();
        EmailRules.Settle(m, EmailSendOutcome.Permanent, error, Now, new EmailOptions());
        return m;
    }

    private static EmailMessage Message() => new()
    {
        Id = 1,
        To = "a@x.nl",
        Subject = "Hi",
        HtmlBody = "<p>token</p>",
        TextBody = "token",
        Status = OutboxStatus.Processing,
        NextAttemptAt = Now
    };
}
