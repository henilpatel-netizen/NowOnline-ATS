using Ats.Application.Email;
using Ats.Domain.Enums;
using Xunit;

namespace Ats.Tests.Email;

public class EmailClaimTests
{
    private static readonly DateTimeOffset Lease = new(2026, 10, 28, 12, 2, 0, TimeSpan.Zero);
    private static readonly EmailClaim Claim = new(1, 1, Lease);

    [Fact]
    public void A_processing_row_with_our_lease_is_still_ours() =>
        Assert.True(Claim.IsOwnedBy(OutboxStatus.Processing, Lease));

    [Fact]
    public void A_row_reclaimed_by_another_worker_is_not_ours() =>
        Assert.False(Claim.IsOwnedBy(OutboxStatus.Processing, Lease.AddSeconds(120)));

    [Theory]
    [InlineData(OutboxStatus.Pending)]
    [InlineData(OutboxStatus.Delivered)]
    [InlineData(OutboxStatus.Failed)]
    public void A_row_no_longer_processing_is_not_ours(OutboxStatus status) =>
        Assert.False(Claim.IsOwnedBy(status, Lease));
}
