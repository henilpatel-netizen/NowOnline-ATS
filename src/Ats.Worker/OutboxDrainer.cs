using Ats.Application.Integration;
using Microsoft.Extensions.Options;

namespace Ats.Worker;

public sealed class OutboxDrainer : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IntegrationOptions _opts;
    private readonly ILogger<OutboxDrainer> _logger;

    public OutboxDrainer(IServiceProvider services, IOptions<IntegrationOptions> opts, ILogger<OutboxDrainer> logger)
    {
        _services = services; _opts = opts.Value; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<OutboxClaim> claims;
                using (var scope = _services.CreateScope())
                {
                    var store = scope.ServiceProvider.GetRequiredService<IOutboxClaimStore>();
                    claims = await store.ClaimDueAsync(_opts.BatchSize, DateTimeOffset.UtcNow, stoppingToken);
                }

                await OutboxBatch.RunAsync(claims,
                    async claim =>
                    {
                        using var ms = _services.CreateScope();
                        return await ms.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessAsync(claim, stoppingToken);
                    },
                    (claim, ex) => RecordFailureAsync(claim, ex, stoppingToken),
                    () => DateTimeOffset.UtcNow,
                    stoppingToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                _logger.LogError(ex, "Outbox drain cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_opts.PollSeconds), stoppingToken);
        }
    }

    private async Task RecordFailureAsync(OutboxClaim claim, Exception ex, CancellationToken stoppingToken)
    {
        _logger.LogError(ex, "Outbox message {MessageId} for tenant {TenantId} failed", claim.Id, claim.TenantId);
        try
        {
            using var scope = _services.CreateScope();
            // The exception text stays in the log: LastError is shown to the tenant's owner.
            await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().RecordFailureAsync(
                claim, $"Processing failed ({ex.GetType().Name}); see the worker log.", stoppingToken);
        }
        catch (Exception recordEx) when (!(recordEx is OperationCanceledException && stoppingToken.IsCancellationRequested))
        {
            _logger.LogError(recordEx,
                "Could not record the failure of outbox message {MessageId} for tenant {TenantId}; the claim lease will return it",
                claim.Id, claim.TenantId);
        }
    }
}
