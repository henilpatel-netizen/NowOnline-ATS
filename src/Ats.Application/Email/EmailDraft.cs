namespace Ats.Application.Email;

public sealed record EmailDraft(string To, string Subject, string HtmlBody, string TextBody)
{
    // Lengths match the EmailMessages columns.
    public const int MaxRecipientLength = 256;
    public const int MaxSubjectLength = 300;

    // The recipient comes from stored data (an address may predate the stricter rule), so an unusable one is
    // skipped by the outbox rather than failing the caller's save.
    public static bool HasValidRecipient(EmailDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.To is { Length: <= MaxRecipientLength } to && EmailRules.IsAddress(to);
    }

    public static void Validate(EmailDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.To, nameof(draft.To));
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Subject, nameof(draft.Subject));
        if (!HasValidRecipient(draft))
            throw new ArgumentException("Recipient is not a valid email address of at most 256 characters.", nameof(draft));
        if (draft.Subject.Length > MaxSubjectLength)
            throw new ArgumentException("Subject is longer than 300 characters.", nameof(draft));
        // A line break in a header would let the subject inject further headers.
        if (draft.Subject.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new ArgumentException("Subject contains a line break.", nameof(draft));
    }
}
