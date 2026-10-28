using System.Globalization;
using System.Net;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;

namespace Ats.Application.Email;

public static partial class EmailTemplates
{
    private static readonly ResourceManager Texts =
        new("Ats.Application.Email.EmailTexts", typeof(EmailTemplates).Assembly);

    // English until per-user localisation (FEAT-5) exists; EmailTexts.nl.resx is kept in step for it.
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en");

    private sealed record Line(string Key, string[] Args);

    private sealed record Button(string LabelKey, string Link);

    private static Line L(string key, params string[] args) => new(key, args);

    public static EmailDraft PasswordReset(string to, string displayName, string tenantName, string link) =>
        Build(to, Subject("PasswordReset_Subject", tenantName),
            [L("Greeting", displayName), L("PasswordReset_Body", tenantName)],
            new Button("PasswordReset_Action", link),
            [L("Expires1Hour"), L("IgnoreIfNotRequested")]);

    public static EmailDraft Invite(string to, string displayName, string tenantName, string inviterName, string link) =>
        Build(to, Subject("Invite_Subject", tenantName),
            [L("Greeting", displayName), L("Invite_Body", inviterName, tenantName)],
            new Button("Invite_Action", link),
            [L("Expires7Days"), L("Invite_Ignore")]);

    public static EmailDraft EmailChangeVerify(string to, string displayName, string tenantName, string link) =>
        Build(to, Subject("EmailChangeVerify_Subject", tenantName),
            [L("Greeting", displayName), L("EmailChangeVerify_Body", tenantName)],
            new Button("EmailChangeVerify_Action", link),
            [L("Expires24Hours"), L("IgnoreIfNotRequested")]);

    public static EmailDraft EmailChangeNotice(string oldAddress, string displayName, string tenantName, string newAddress) =>
        Build(oldAddress, Subject("EmailChangeNotice_Subject", tenantName),
            [L("Greeting", displayName), L("EmailChangeNotice_Body", tenantName, newAddress), L("ContactAdministrator")]);

    public static EmailDraft PasswordChanged(string to, string displayName, string tenantName) =>
        Build(to, Subject("PasswordChanged_Subject", tenantName),
            [L("Greeting", displayName), L("PasswordChanged_Body", tenantName), L("ContactAdministrator")]);

    public static EmailDraft AccountDeactivated(string to, string displayName, string tenantName) =>
        Build(to, Subject("AccountDeactivated_Subject", tenantName),
            [L("Greeting", displayName), L("AccountDeactivated_Body", tenantName), L("ContactAdministrator")]);

    public static EmailDraft NewApplication(string to, string tenantName, string candidateName, string jobTitle, string link) =>
        Build(to, Subject("NewApplication_Subject", jobTitle),
            [L("GreetingGeneric"), L("NewApplication_Body", candidateName, jobTitle, tenantName)],
            new Button("NewApplication_Action", link));

    public static EmailDraft StageChanged(string to, string tenantName, string candidateName, string jobTitle, string stageName, string link) =>
        Build(to, Subject("StageChanged_Subject", candidateName, stageName),
            [L("GreetingGeneric"), L("StageChanged_Body", candidateName, stageName, jobTitle, tenantName)],
            new Button("StageChanged_Action", link));

    private static EmailDraft Build(string to, string subject, Line[] intro, Button? action = null, Line[]? outro = null)
    {
        var html = new StringBuilder()
            .Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>")
            .Append(WebUtility.HtmlEncode(subject))
            .Append("</title></head><body style=\"font-family: Arial, sans-serif; line-height: 1.5;\">");
        var text = new StringBuilder();

        void Paragraph(Line line)
        {
            html.Append("<p>").Append(Html(line)).Append("</p>");
            text.Append(Text(line.Key, line.Args)).Append("\n\n");
        }

        foreach (var line in intro) Paragraph(line);

        if (action is not null)
        {
            var href = WebUtility.HtmlEncode(action.Link);
            var label = WebUtility.HtmlEncode(Text(action.LabelKey));
            html.Append("<p><a href=\"").Append(href).Append("\">").Append(label).Append("</a></p>")
                .Append("<p>").Append(WebUtility.HtmlEncode(Text("LinkFallback"))).Append("<br>")
                .Append(href).Append("</p>");
            text.Append(Text(action.LabelKey)).Append(":\n").Append(action.Link).Append("\n\n");
        }

        foreach (var line in outro ?? []) Paragraph(line);

        html.Append("</body></html>");
        return new EmailDraft(to, subject, html.ToString(), text.ToString().TrimEnd() + "\n");
    }

    private static string Text(string key, params string[] args) =>
        string.Format(Culture, Resource(key), args);

    public const int MaxSubjectLength = EmailDraft.MaxSubjectLength;

    // A subject is one header line, so a line break in a job title or name must not reach it. Names and stage
    // names can be long enough together to pass the column limit, so the end is cut and marked "...".
    private static string Subject(string key, params string[] args)
    {
        var subject = Text(key, [.. args.Select(a => ControlCharacters().Replace(a, " "))]);
        if (subject.Length <= MaxSubjectLength) return subject;
        var cut = MaxSubjectLength - 3;
        if (char.IsHighSurrogate(subject[cut - 1])) cut--;
        return string.Concat(subject.AsSpan(0, cut), "...");
    }

    [GeneratedRegex(@"\p{Cc}+")]
    private static partial Regex ControlCharacters();

    // The resource text is trusted, but encoded too so an apostrophe or ampersand renders as text.
    private static string Html(Line line) =>
        string.Format(Culture, WebUtility.HtmlEncode(Resource(line.Key)),
            line.Args.Select(a => (object)WebUtility.HtmlEncode(a)).ToArray());

    private static string Resource(string key) =>
        Texts.GetString(key, Culture) ?? throw new InvalidOperationException($"Missing email text '{key}'.");
}
