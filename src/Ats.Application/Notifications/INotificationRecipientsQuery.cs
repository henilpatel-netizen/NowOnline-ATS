namespace Ats.Application.Notifications;

public sealed record Recipient(int UserId, string Email);

// Read under the normal tenant filter. Only active users who accepted their invitation are returned.
public interface INotificationRecipientsQuery
{
    // Recruiters plus the job's assigned HiringManagers; Owners instead of Recruiters when the tenant has none.
    Task<IReadOnlyList<Recipient>> NewApplicationAsync(int jobId, CancellationToken ct = default);
    // The job's assigned HiringManagers.
    Task<IReadOnlyList<Recipient>> StageChangeAsync(int jobId, CancellationToken ct = default);
}
