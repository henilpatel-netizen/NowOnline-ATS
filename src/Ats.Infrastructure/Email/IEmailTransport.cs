namespace Ats.Infrastructure.Email;

// Throws MailKit's SmtpCommandException / connection / auth exceptions, or InvalidRecipientException;
// EmailProcessor classifies them.
public interface IEmailTransport
{
    Task SendAsync(string to, string subject, string? html, string? text, CancellationToken ct);
}

// Carries no address on purpose: it ends up in logs.
public sealed class InvalidRecipientException() : Exception("Invalid recipient address.");
