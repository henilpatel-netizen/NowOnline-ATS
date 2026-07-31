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

                // Per-application ordering: process each application's messages oldest-first and stop
                // the chain on the first non-delivered outcome so message N+1 never precedes N.
                foreach (var group in claims.GroupBy(c => (c.TenantId, c.ApplicationId)))
                {
                    foreach (var claim in group.OrderBy(c => c.Id))
                    {
                        using var ms = _services.CreateScope();
                        var processor = ms.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                        var outcome = await processor.ProcessAsync(claim, stoppingToken);
                        if (outcome != OutboxOutcome.Delivered) break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox drain cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_opts.PollSeconds), stoppingToken);
        }
    }
}
