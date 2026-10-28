using Ats.Application.Email;
using Xunit;

namespace Ats.Tests.Email;

public class EmailDraftTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_blank_recipient(string to) =>
        Assert.Throws<ArgumentException>(() => EmailDraft.Validate(new EmailDraft(to, "Subject", "<p>x</p>", "x")));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_blank_subject(string subject) =>
        Assert.Throws<ArgumentException>(() => EmailDraft.Validate(new EmailDraft("a@example.com", subject, "<p>x</p>", "x")));

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("a@")]
    public void Validate_rejects_invalid_recipient(string to) =>
        Assert.Throws<ArgumentException>(() => EmailDraft.Validate(new EmailDraft(to, "Subject", "<p>x</p>", "x")));

    [Fact]
    public void Validate_rejects_recipient_over_256() =>
        Assert.Throws<ArgumentException>(() => EmailDraft.Validate(
            new EmailDraft(new string('a', 245) + "@example.com", "Subject", "<p>x</p>", "x")));

    [Fact]
    public void Validate_rejects_subject_over_300() =>
        Assert.Throws<ArgumentException>(() => EmailDraft.Validate(
            new EmailDraft("a@example.com", new string('s', 301), "<p>x</p>", "x")));

    [Theory]
    [InlineData("a\r\nBcc: x")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public void Validate_rejects_subject_with_a_line_break(string subject) =>
        Assert.Throws<ArgumentException>(() => EmailDraft.Validate(new EmailDraft("a@example.com", subject, "<p>x</p>", "x")));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("a@")]
    [InlineData("a@b.c\r\nBcc: x@y.z")]
    public void HasValidRecipient_is_false_for_an_unusable_address(string to) =>
        Assert.False(EmailDraft.HasValidRecipient(new EmailDraft(to, "Subject", "<p>x</p>", "x")));

    [Fact]
    public void HasValidRecipient_is_false_over_256() =>
        Assert.False(EmailDraft.HasValidRecipient(new EmailDraft(new string('a', 245) + "@example.com", "Subject", "<p>x</p>", "x")));

    [Fact]
    public void HasValidRecipient_is_true_for_a_valid_address() =>
        Assert.True(EmailDraft.HasValidRecipient(new EmailDraft("a@example.com", "Subject", "<p>x</p>", "x")));

    // The recipient may come from stored data; the subject is ours, so a bad one is still a programmer error.
    [Fact]
    public void HasValidRecipient_ignores_the_subject() =>
        Assert.True(EmailDraft.HasValidRecipient(new EmailDraft("a@example.com", "", "<p>x</p>", "x")));

    [Fact]
    public void Validate_accepts_subject_of_300() =>
        EmailDraft.Validate(new EmailDraft("a@example.com", new string('s', 300), "<p>x</p>", "x"));

    [Fact]
    public void Validate_accepts_complete_draft() =>
        EmailDraft.Validate(new EmailDraft("a@example.com", "Subject", "<p>x</p>", "x"));
}
