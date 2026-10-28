using Ats.Application.Abstractions;
using Ats.Application.Notifications;
using Ats.Domain.Entities;

namespace Ats.Tests.Fakes;

// Runs the production NotificationRecipientFilter over in-memory users, so notifier tests cover the recipient rules.
public sealed class FakeNotificationRecipientsQuery : INotificationRecipientsQuery
{
    public List<AppUser> Users { get; } = new();
    public List<JobHiringManager> Assignments { get; } = new();

    public Task<IReadOnlyList<Recipient>> NewApplicationAsync(int jobId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Recipient>>(
            NotificationRecipientFilter.NewApplication(Users.AsQueryable(), AssignedTo(jobId)).ToList());

    public Task<IReadOnlyList<Recipient>> StageChangeAsync(int jobId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Recipient>>(
            NotificationRecipientFilter.StageChange(Users.AsQueryable(), AssignedTo(jobId)).ToList());

    public AppUser Add(int id, string role, bool active = true, bool accepted = true)
    {
        var user = new AppUser
        {
            Id = id,
            Email = $"user{id}@acme.test",
            DisplayName = $"User {id}",
            Role = role,
            IsActive = active,
            PasswordHash = accepted ? "hash" : ""
        };
        Users.Add(user);
        return user;
    }

    public void Assign(int jobId, int userId) => Assignments.Add(new JobHiringManager { JobId = jobId, UserId = userId });

    private IQueryable<int> AssignedTo(int jobId) =>
        Assignments.Where(a => a.JobId == jobId).Select(a => a.UserId).AsQueryable();
}

public sealed class NullHiringNotifier : IHiringNotifier
{
    public Task StageNewApplicationAsync(int jobId, string jobTitle, string candidateName, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task StageStageChangedAsync(int jobId, string jobTitle, string candidateName, string stageName, int? movedByUserId,
        CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeTenantContext : ITenantContext
{
    public int? CurrentTenantId { get; set; } = 1;
    public bool HasTenant => CurrentTenantId is not null;
}
