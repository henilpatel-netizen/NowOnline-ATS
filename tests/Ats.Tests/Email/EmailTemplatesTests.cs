using System.Collections;
using System.Globalization;
using System.Net;
using System.Resources;
using Ats.Application.Email;
using Xunit;

namespace Ats.Tests.Email;

public class EmailTemplatesTests
{
    private const string To = "user@example.com";
    private const string Evil = "<b>x</b>";
    private const string EncodedEvil = "&lt;b&gt;x&lt;/b&gt;";
    private const string Link = "https://ats.example.com/account/reset?token=abc-123&x=1";

    // Every template, with the value under test injected into each free-text argument.
    private static readonly Dictionary<string, Func<string, EmailDraft>> Render = new()
    {
        ["PasswordReset"] = v => EmailTemplates.PasswordReset(To, v, v, Link),
        ["Invite"] = v => EmailTemplates.Invite(To, v, v, v, Link),
        ["EmailChangeVerify"] = v => EmailTemplates.EmailChangeVerify(To, v, v, Link),
        ["EmailChangeNotice"] = v => EmailTemplates.EmailChangeNotice(To, v, v, v),
        ["PasswordChanged"] = v => EmailTemplates.PasswordChanged(To, v, v),
        ["AccountDeactivated"] = v => EmailTemplates.AccountDeactivated(To, v, v),
        ["NewApplication"] = v => EmailTemplates.NewApplication(To, v, v, v, Link),
        ["StageChanged"] = v => EmailTemplates.StageChanged(To, v, v, v, v, Link),
    };

    private static readonly HashSet<string> WithLink =
        ["PasswordReset", "Invite", "EmailChangeVerify", "NewApplication", "StageChanged"];

    public static TheoryData<string> Templates() => new(Render.Keys);

    public static TheoryData<string> LinkTemplates() => new(WithLink);

    [Theory]
    [MemberData(nameof(Templates))]
    public void Html_body_encodes_user_values_and_text_body_keeps_them_raw(string template)
    {
        var draft = Render[template](Evil);

        Assert.Contains(EncodedEvil, draft.HtmlBody);
        Assert.DoesNotContain(Evil, draft.HtmlBody);
        Assert.Contains(Evil, draft.TextBody);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Recipient_and_subject_are_set(string template)
    {
        var draft = Render[template]("Acme");

        Assert.Equal(To, draft.To);
        Assert.False(string.IsNullOrWhiteSpace(draft.Subject));
        EmailDraft.Validate(draft);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Control_characters_in_user_values_become_one_space_in_the_subject(string template)
    {
        var draft = Render[template]("Dev\r\nBcc: x@y.z\t\0");

        Assert.DoesNotContain('\r', draft.Subject);
        Assert.DoesNotContain('\n', draft.Subject);
        Assert.DoesNotContain('\t', draft.Subject);
        Assert.DoesNotContain('\0', draft.Subject);
        Assert.Contains("Dev Bcc: x@y.z", draft.Subject);
        EmailDraft.Validate(draft);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Long_user_values_are_cut_so_the_subject_fits(string template)
    {
        var draft = Render[template](new string('x', 400));

        Assert.True(draft.Subject.Length <= EmailTemplates.MaxSubjectLength);
        EmailDraft.Validate(draft);
    }

    [Fact]
    public void A_stage_change_subject_with_a_long_name_and_stage_ends_with_an_ellipsis()
    {
        var draft = EmailTemplates.StageChanged(To, "Acme", new string('n', 201), "Dev", new string('s', 120), Link);

        Assert.Equal(EmailTemplates.MaxSubjectLength, draft.Subject.Length);
        Assert.EndsWith("...", draft.Subject);
        EmailDraft.Validate(draft);
    }

    // An emoji is two chars; cutting between them would leave a lone high surrogate, which is not valid text.
    [Fact]
    public void The_cut_never_splits_a_surrogate_pair()
    {
        var prefix = EmailTemplates.NewApplication(To, "Acme", "Ada", "", Link).Subject.Length;
        // The pair sits at indexes 296 and 297 of the uncut subject, straddling the cut at 297.
        var title = new string('x', 296 - prefix) + "\U0001F600" + new string('y', 50);

        var subject = EmailTemplates.NewApplication(To, "Acme", "Ada", title, Link).Subject;

        Assert.True(subject.Length <= EmailTemplates.MaxSubjectLength);
        Assert.EndsWith("...", subject);
        for (var i = 0; i < subject.Length; i++)
            Assert.False(char.IsHighSurrogate(subject[i]) && (i + 1 == subject.Length || !char.IsLowSurrogate(subject[i + 1])),
                $"Lone high surrogate at {i}");
    }

    [Fact]
    public void A_short_subject_is_left_whole()
    {
        var draft = EmailTemplates.StageChanged(To, "Acme", "Ada", "Dev", "Interview", Link);

        Assert.DoesNotContain("...", draft.Subject);
        Assert.Contains("Ada", draft.Subject);
        Assert.Contains("Interview", draft.Subject);
    }

    [Fact]
    public void A_job_title_with_a_line_break_yields_a_single_line_subject()
    {
        var draft = EmailTemplates.NewApplication(To, "Acme", "Ada", "Dev\r\nBcc: x@y.z", Link);

        Assert.DoesNotContain('\r', draft.Subject);
        Assert.DoesNotContain('\n', draft.Subject);
    }

    [Theory]
    [MemberData(nameof(LinkTemplates))]
    public void Link_appears_in_both_bodies(string template)
    {
        var draft = Render[template]("Acme");

        Assert.Contains($"href=\"{WebUtility.HtmlEncode(Link)}\"", draft.HtmlBody);
        Assert.Contains(Link, draft.TextBody);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Html_body_declares_a_language_and_no_external_images(string template)
    {
        var draft = Render[template]("Acme");

        Assert.Contains("<html lang=\"en\">", draft.HtmlBody);
        Assert.DoesNotContain("<img", draft.HtmlBody);
    }

    [Fact]
    public void Link_lifetimes_and_ignore_lines_are_stated()
    {
        var reset = EmailTemplates.PasswordReset(To, "Ada", "Acme", Link).TextBody;
        Assert.Contains("expires in 1 hour", reset);
        Assert.Contains("If you did not request this, ignore this email.", reset);

        Assert.Contains("expires in 7 days", EmailTemplates.Invite(To, "Ada", "Acme", "Bo", Link).TextBody);

        var verify = EmailTemplates.EmailChangeVerify(To, "Ada", "Acme", Link).TextBody;
        Assert.Contains("expires in 24 hours", verify);
        Assert.Contains("If you did not request this, ignore this email.", verify);
    }

    [Fact]
    public void Notices_tell_the_user_to_contact_their_administrator()
    {
        Assert.Contains("contact your administrator", EmailTemplates.PasswordChanged(To, "Ada", "Acme").TextBody);
        Assert.Contains("contact your administrator", EmailTemplates.AccountDeactivated(To, "Ada", "Acme").TextBody);
        var notice = EmailTemplates.EmailChangeNotice(To, "Ada", "Acme", "new@example.com").TextBody;
        Assert.Contains("contact your administrator", notice);
        Assert.Contains("new@example.com", notice);
    }

    [Fact]
    public void Renders_in_English_whatever_the_current_culture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nl");
            Assert.Contains("expires in 1 hour", EmailTemplates.PasswordReset(To, "Ada", "Acme", Link).TextBody);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Dutch_texts_have_exactly_the_English_keys()
    {
        var resources = new ResourceManager("Ats.Application.Email.EmailTexts", typeof(EmailTemplates).Assembly);

        var en = Keys(resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false));
        var nl = Keys(resources.GetResourceSet(CultureInfo.GetCultureInfo("nl"), createIfNotExists: true, tryParents: false));

        Assert.NotEmpty(en);
        Assert.Equal(en, nl);
    }

    private static SortedSet<string> Keys(ResourceSet? set)
    {
        Assert.NotNull(set);
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Value as string), $"Empty text for {entry.Key}");
            keys.Add((string)entry.Key);
        }
        return keys;
    }
}
