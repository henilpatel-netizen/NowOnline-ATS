using Ats.Application.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Ats.Infrastructure.Email;

public sealed class MailKitEmailTransport : IEmailTransport
{
    private readonly EmailOptions _opts;

    public MailKitEmailTransport(IOptions<EmailOptions> opts) => _opts = opts.Value;

    public async Task SendAsync(string to, string subject, string? html, string? text, CancellationToken ct)
    {
        if (!MailboxAddress.TryParse(to, out var recipient))
            throw new InvalidRecipientException();

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
        message.To.Add(recipient);
        message.Subject = subject;
        message.Body = new BodyBuilder { TextBody = text, HtmlBody = html }.ToMessageBody();

        if (!string.IsNullOrWhiteSpace(_opts.PickupDirectory))
        {
            Directory.CreateDirectory(_opts.PickupDirectory);
            var path = Path.Combine(_opts.PickupDirectory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
            await message.WriteToAsync(path, ct);
            return;
        }

        var smtp = _opts.Smtp;
        // The timeout applies to each network operation, not to the whole send. EmailDrainer only starts
        // a send with EmailRules.WorstCaseSendSeconds of lease left (connect, auth, send at 30 s each); a
        // server that stalls on more steps than that can still outlive the lease.
        using var client = new SmtpClient { Timeout = 30_000 };
        await client.ConnectAsync(smtp.Host, smtp.Port, Enum.Parse<SecureSocketOptions>(smtp.Security, ignoreCase: true), ct);
        if (!string.IsNullOrEmpty(smtp.UserName))
            await client.AuthenticateAsync(smtp.UserName, smtp.Password, ct);
        await client.SendAsync(message, ct);
        // No DisconnectAsync: a failed QUIT after a sent message must not turn it into a retry. Dispose
        // closes the connection.
    }
}
