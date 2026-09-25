using Ats.Application.Integration;
using Xunit;

namespace Ats.Tests.Integration;

public class OutboxBatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Lease = Start.AddSeconds(300);

    private readonly List<int> _processed = [];
    private readonly List<(int Id, Exception Error)> _failures = [];
    private DateTimeOffset _now = Start;

    private Task RunAsync(IEnumerable<OutboxClaim> claims, Func<OutboxClaim, OutboxOutcome> process, CancellationToken ct = default) =>
        OutboxBatch.RunAsync(claims,
            claim => { _processed.Add(claim.Id); return Task.FromResult(process(claim)); },
            (claim, ex) => { _failures.Add((claim.Id, ex)); return Task.CompletedTask; },
            () => _now,
            ct);

    private static OutboxClaim Claim(int id, int applicationId, int tenantId = 1, DateTimeOffset? lease = null) =>
        new(id, tenantId, applicationId, lease ?? Lease);

    [Fact]
    public async Task An_expired_lease_is_not_processed_and_the_others_continue()
    {
        await RunAsync([Claim(1, 10, lease: Start), Claim(3, 20)], _ => OutboxOutcome.Delivered);

        Assert.Equal([3], _processed);
        Assert.Empty(_failures);
    }

    [Fact]
    public async Task A_lease_that_expires_mid_batch_skips_the_remaining_claims()
    {
        await RunAsync([Claim(1, 10), Claim(2, 20), Claim(3, 30)], _ =>
        {
            _now = Lease.AddSeconds(1); // the first call hung past the lease
            return OutboxOutcome.Delivered;
        });

        Assert.Equal([1], _processed);
        Assert.Empty(_failures);
    }

    [Fact]
    public async Task A_throwing_message_is_recorded_and_does_not_stall_other_applications()
    {
        var boom = new InvalidOperationException("database down");

        await RunAsync([Claim(1, 10), Claim(3, 20, tenantId: 2)],
            c => c.Id == 1 ? throw boom : OutboxOutcome.Delivered);

        Assert.Equal([1, 3], _processed);
        Assert.Equal((1, (Exception)boom), Assert.Single(_failures));
    }

    [Fact]
    public async Task Cancellation_on_shutdown_is_rethrown_not_recorded()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RunAsync([Claim(1, 10), Claim(2, 20)], _ => throw new OperationCanceledException(cts.Token), cts.Token));

        Assert.Empty(_failures);
        Assert.Equal([1], _processed);
    }

    [Fact]
    public async Task Cancellation_while_not_shutting_down_is_a_message_failure()
    {
        await RunAsync([Claim(1, 10), Claim(2, 20)],
            c => c.Id == 1 ? throw new TaskCanceledException("command timeout") : OutboxOutcome.Delivered);

        Assert.Equal([1, 2], _processed);
        Assert.Equal(1, Assert.Single(_failures).Id);
    }
}
