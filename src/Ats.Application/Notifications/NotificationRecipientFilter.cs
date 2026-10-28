using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Notifications;

// Plain LINQ, applied by NotificationRecipientsQuery to the tenant-filtered Users, so the rules are unit-tested
// without a database (the UserListFilter / JobScopeFilter exception). assignedUserIds must come through db.Jobs so a
// soft-deleted job has no team.
public static class NotificationRecipientFilter
{
    // Inactive users and invite-pending users (no password yet) never get mail.
    public static IQueryable<AppUser> Reachable(this IQueryable<AppUser> users) =>
        users.Where(u => u.IsActive && u.PasswordHash != "");

    public static IQueryable<Recipient> NewApplication(IQueryable<AppUser> users, IQueryable<int> assignedUserIds)
    {
        var reachable = users.Reachable();
        return reachable
            .Where(u => u.Role == AtsRole.Recruiter
                || (u.Role == AtsRole.Owner && !reachable.Any(r => r.Role == AtsRole.Recruiter))
                || (u.Role == AtsRole.HiringManager && assignedUserIds.Contains(u.Id)))
            .Select(u => new Recipient(u.Id, u.Email));
    }

    public static IQueryable<Recipient> StageChange(IQueryable<AppUser> users, IQueryable<int> assignedUserIds) =>
        users.Reachable()
            .Where(u => u.Role == AtsRole.HiringManager && assignedUserIds.Contains(u.Id))
            .Select(u => new Recipient(u.Id, u.Email));
}
