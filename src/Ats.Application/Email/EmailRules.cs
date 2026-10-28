using System.Net.Mail;
using System.Text.RegularExpressions;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Email;

public enum EmailSendOutcome { Sent, Transient, Permanent }

public static partial class EmailRules
{
    // Called only for an SMTP error reply; connection, protocol, auth, IO and timeout failures are
    // always Transient. Only a 5xx rejection of the recipient is final. Every other reply retries:
    // 4xx, and 5xx about the sender, the message or the session (530 auth required, 552/554). An
    // enhanced status 5.4.x retries even on RCPT: Gmail answers its daily quota with "550 5.4.5".
    public static EmailSendOutcome Classify(int smtpStatusCode, bool recipientRejected, string? enhancedStatus) =>
        recipientRejected && smtpStatusCode is >= 500 and < 600
            && enhancedStatus?.StartsWith("5.4.", StringComparison.Ordinal) != true
            ? EmailSendOutcome.Permanent
            : EmailSendOutcome.Transient;

    // The RFC 3463 enhanced status code ("5.4.5") in a server reply, or null. The reply text itself can
    // echo the recipient address, so only this code may be logged or stored.
    public static string? EnhancedStatus(string? serverText) =>
        serverText is null ? null : EnhancedStatusPattern().Match(serverText) is { Success: true } m ? m.Value : null;

    [GeneratedRegex(@"(?<![\d.])[245]\.\d{1,3}\.\d{1,3}(?![\d.])")]
    private static partial Regex EnhancedStatusPattern();

    // Connect, authenticate and send can each take one full 30 s SMTP timeout; a send is only started
    // with that much lease left, so it cannot outlive the claim and be sent again by another pass.
    public const int WorstCaseSendSeconds = 90;

    public static bool HasLeaseFor(DateTimeOffset lease, DateTimeOffset now) =>
        lease - now >= TimeSpan.FromSeconds(WorstCaseSendSeconds);

    // A bare address only (no display name), as System.Net.Mail parses it.
    public static bool IsAddress(string? value) =>
        !string.IsNullOrWhiteSpace(value) && MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;

    public static bool HasBody(string? html, string? text) => html is not null || text is not null;

    public static (string To, string Subject) Route(string to, string subject, string? redirectAllTo) =>
        string.IsNullOrWhiteSpace(redirectAllTo) ? (to, subject) : (redirectAllTo, $"[to: {to}] {subject}");

    public static double BackoffSeconds(int attempts, EmailOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return Math.Min(o.MaxBackoffSeconds, o.BaseBackoffSeconds * Math.Pow(2, attempts));
    }

    // A finished message (Delivered or Failed) loses its bodies, so no live link or token stays in the
    // database after sending. error must hold only the SMTP status and exception type.
    public static void Settle(EmailMessage msg, EmailSendOutcome outcome, string? error, DateTimeOffset now, EmailOptions o)
    {
        ArgumentNullException.ThrowIfNull(msg);
        ArgumentNullException.ThrowIfNull(o);
        msg.LastError = error is { Length: > 1000 } ? error[..1000] : error;

        if (outcome == EmailSendOutcome.Transient)
        {
            msg.Attempts++;
            if (msg.Attempts < o.MaxAttempts)
            {
                msg.Status = OutboxStatus.Pending;
                msg.NextAttemptAt = now.AddSeconds(BackoffSeconds(msg.Attempts, o));
                return;
            }
        }

        msg.Status = outcome == EmailSendOutcome.Sent ? OutboxStatus.Delivered : OutboxStatus.Failed;
        msg.HtmlBody = null;
        msg.TextBody = null;
    }
}
