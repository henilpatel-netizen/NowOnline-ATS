namespace Ats.Application.Email;

// Section "Email", bound by Ats.Worker. Gmail sends as the authenticated account, so FromAddress must
// equal Smtp:UserName there. UserName, Password, FromAddress and RedirectAllTo come from user-secrets
// or the environment, never from appsettings.
public sealed class EmailOptions
{
    public int PollSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 8;
    public int BaseBackoffSeconds { get; set; } = 30;
    public int MaxBackoffSeconds { get; set; } = 3600;
    public int ClaimLeaseSeconds { get; set; } = 120;
    public SmtpSettings Smtp { get; set; } = new();
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Ats";
    // Development only: every message goes here instead, subject prefixed with the real recipient.
    public string? RedirectAllTo { get; set; }
    // When set, messages are written as .eml files here instead of sent.
    public string? PickupDirectory { get; set; }

    public static readonly string[] SecurityOptions = ["None", "StartTls", "SslOnConnect"];

    public static IEnumerable<string> Validate(EmailOptions o, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (!string.IsNullOrWhiteSpace(o.RedirectAllTo) && !isDevelopment)
            yield return "Email:RedirectAllTo is only allowed in Development.";
        if (!string.IsNullOrWhiteSpace(o.RedirectAllTo) && !EmailRules.IsAddress(o.RedirectAllTo))
            yield return "Email:RedirectAllTo is not a valid email address.";
        if (string.IsNullOrWhiteSpace(o.PickupDirectory) && string.IsNullOrWhiteSpace(o.Smtp.Host))
            yield return "Email:Smtp:Host (or Email:PickupDirectory) is required.";
        if (string.IsNullOrWhiteSpace(o.FromAddress))
            yield return "Email:FromAddress is required.";
        else if (!EmailRules.IsAddress(o.FromAddress))
            yield return "Email:FromAddress is not a valid email address.";
        if (!SecurityOptions.Contains(o.Smtp.Security, StringComparer.OrdinalIgnoreCase))
            yield return $"Email:Smtp:Security must be one of {string.Join(", ", SecurityOptions)}.";
        if (o.Smtp.Port is < 1 or > 65535)
            yield return "Email:Smtp:Port must be between 1 and 65535.";
        if (o.PollSeconds < 1)
            yield return "Email:PollSeconds must be at least 1.";
        if (o.BatchSize is < 1 or > 500)
            yield return "Email:BatchSize must be between 1 and 500.";
        if (o.MaxAttempts < 1)
            yield return "Email:MaxAttempts must be at least 1.";
        if (o.BaseBackoffSeconds < 1)
            yield return "Email:BaseBackoffSeconds must be at least 1.";
        if (o.MaxBackoffSeconds < o.BaseBackoffSeconds)
            yield return "Email:MaxBackoffSeconds must be at least Email:BaseBackoffSeconds.";
        if (o.ClaimLeaseSeconds < 120)
            yield return "Email:ClaimLeaseSeconds must be at least 120.";
    }
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}
