using System.Net.Mail;

namespace Ats.Application.Common;

// The one rule for a user's sign-in address: users, account links and the Users screen share it.
public static class EmailAddress
{
    public const string Invalid = "Enter a valid email address.";
    public const string Duplicate = "That email address is already registered.";

    public static string Normalise(string? email) => email?.Trim().ToLowerInvariant() ?? "";

    // A bare, already normalised address of at most 256 characters (the Users.Email column).
    public static bool IsValid(string email) =>
        email.Length is > 0 and <= 256 && MailAddress.TryCreate(email, out var a) && a.Address == email;
}
