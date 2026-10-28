using System.Net.Sockets;
using Ats.Application.Email;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ats.Infrastructure.Email;

// EmailMessage is not tenant-filtered, so the worker tenant context is never set here.
// Never log SmtpCommandException.Message: RCPT replies echo the recipient address.
public sealed class EmailProcessor : IEmailProcessor
{
    private readonly AtsDbContext _db;
    private readonly IEmailTransport _transport;
    private readonly EmailOptions _opts;
    private readonly ILogger<EmailProcessor> _logger;

    public EmailProcessor(AtsDbContext db, IEmailTransport transport, IOptions<EmailOptions> opts, ILogger<EmailProcessor> logger)
    {
        _db = db; _transport = transport; _opts = opts.Value; _logger = logger;
    }

    public async Task<EmailSendOutcome?> ProcessAsync(EmailClaim claim, CancellationToken ct = default)
    {
        var msg = await LoadClaimedAsync(claim, ct);
        if (msg is null)
            return null;

        EmailSendOutcome outcome;
        string? error = null;
        SmtpErrorCode? smtpErrorCode = null;
        Exception? connectionFailure = null;

        if (!EmailRules.HasBody(msg.HtmlBody, msg.TextBody))
        {
            outcome = EmailSendOutcome.Permanent;
            error = "No body";
        }
        else
        {
            var (to, subject) = EmailRules.Route(msg.To, msg.Subject, _opts.RedirectAllTo);
            try
            {
                await _transport.SendAsync(to, subject, msg.HtmlBody, msg.TextBody, ct);
                outcome = EmailSendOutcome.Sent;
            }
            catch (InvalidRecipientException)
            {
                outcome = EmailSendOutcome.Permanent;
                error = "Invalid recipient address";
            }
            catch (SmtpCommandException ex)
            {
                smtpErrorCode = ex.ErrorCode;
                var enhanced = EmailRules.EnhancedStatus(ex.Message);
                outcome = EmailRules.Classify((int)ex.StatusCode, ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted, enhanced);
                var status = enhanced is null ? $"{(int)ex.StatusCode}" : $"{(int)ex.StatusCode} {enhanced}";
                error = $"SMTP {status} {ex.ErrorCode} ({ex.GetType().Name})";
            }
            catch (Exception ex) when (IsConnectionOrAuthFailure(ex, ct))
            {
                outcome = EmailSendOutcome.Transient;
                error = ex.GetType().Name;
                connectionFailure = ex;
            }
        }

        // Not cancellable once sent: the outcome must be recorded.
        EmailRules.Settle(msg, outcome, error, DateTimeOffset.UtcNow, _opts);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (outcome == EmailSendOutcome.Sent)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("Email {MessageId} for tenant {TenantId}: claim lost after send; message may be sent again",
                        msg.Id, msg.TenantId);
            }
            else
            {
                LogClaimLost(msg.Id, msg.TenantId, outcome);
            }
            return null;
        }

        Log(msg, outcome, error, smtpErrorCode, connectionFailure);
        return outcome;
    }

    public async Task RecordFailureAsync(EmailClaim claim, string error, CancellationToken ct = default)
    {
        var msg = await LoadClaimedAsync(claim, ct);
        if (msg is null)
            return;
        EmailRules.Settle(msg, EmailSendOutcome.Transient, error, DateTimeOffset.UtcNow, _opts);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogClaimLost(msg.Id, msg.TenantId, EmailSendOutcome.Transient);
        }
    }

    private void LogClaimLost(int messageId, int? tenantId, EmailSendOutcome outcome)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Email {MessageId} for tenant {TenantId}: claim lost, outcome {Outcome} not recorded",
                messageId, tenantId, outcome);
    }

    private async Task<EmailMessage?> LoadClaimedAsync(EmailClaim claim, CancellationToken ct)
    {
        var msg = await _db.EmailMessages.FirstOrDefaultAsync(m => m.Id == claim.Id, ct);
        // Only a row still carrying our lease is ours; anything else was released or reclaimed.
        return msg is not null && claim.IsOwnedBy(msg.Status, msg.NextAttemptAt) ? msg : null;
    }

    private void Log(EmailMessage msg, EmailSendOutcome outcome, string? error, SmtpErrorCode? smtpErrorCode, Exception? connectionFailure)
    {
        // Attempts counts transient failures only; a Permanent outcome on the first try leaves it at 0.
        const string notSent = "Email {MessageId} for tenant {TenantId} not sent ({Outcome}, {Error}, {SmtpErrorCode}); now {Status}, transient failures {Attempts}";
        if (outcome == EmailSendOutcome.Sent)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Email {MessageId} for tenant {TenantId} sent", msg.Id, msg.TenantId);
        }
        else if (msg.Status == OutboxStatus.Failed)
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError(connectionFailure, notSent, msg.Id, msg.TenantId, outcome, error, smtpErrorCode, msg.Status, msg.Attempts);
        }
        else if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(connectionFailure, notSent, msg.Id, msg.TenantId, outcome, error, smtpErrorCode, msg.Status, msg.Attempts);
        }
    }

    private static bool IsConnectionOrAuthFailure(Exception ex, CancellationToken ct) => ex switch
    {
        AuthenticationException or SslHandshakeException or ProtocolException
            or ServiceNotConnectedException or ServiceNotAuthenticatedException
            or IOException or SocketException or TimeoutException => true,
        // A MailKit timeout surfaces as a cancellation nobody asked for; a shutdown must propagate.
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false
    };
}
