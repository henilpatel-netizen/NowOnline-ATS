namespace Ats.Application.Users;

public static class UserAuditSummary
{
    // Names the user by the email they had before the change, since that is what the reader knows them by.
    public static string Updated(UserListItem before, UserListItem after, IReadOnlyCollection<string> changed)
    {
        var parts = new List<string>(3);
        if (changed.Contains(UserField.Email)) parts.Add($"email to '{after.Email}'");
        if (changed.Contains(UserField.Role)) parts.Add($"role to {after.Role}");
        if (changed.Contains(UserField.Name)) parts.Add("name");
        return $"Updated '{before.Email}': {string.Join(", ", parts)}";
    }
}
