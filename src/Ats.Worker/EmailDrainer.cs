using Ats.Application.Email;
using Microsoft.Extensions.Options;

namespace Ats.Worker;

public sealed class EmailDrainer : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly EmailOptions _opts;
    private readonly ILogger<EmailDrainer> _logger;

    public EmailDrainer(IServiceProvider services, IOptions<EmailOptions> opts, ILogger<EmailDrainer> logger)
    {
        _services = services; _opts = opts.Value; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<EmailClaim> claims;
                using (var scope = _services.CreateScope())
                {
                    var store = scope.ServiceProvider.GetRequiredService<IEmailClaimStore>();
                    claims = await store.ClaimDueAsync(_opts.BatchSize, DateTimeOffset.UtcNow, stoppingToken);
                }

                var skipped = 0;
                foreach (var claim in claims)
                {
                    // Too little lease left for a worst-case send: another pass could reclaim and send it
                    // again. Left alone, the claim returns it once the lease expires.
                    if (!EmailRules.HasLeaseFor(claim.Lease, DateTimeOffset.UtcNow))
                    {
                        skipped++;
                        continue;
                    }
                    try
                    {
                        using var ms = _services.CreateScope();
                        await ms.ServiceProvider.GetRequiredService<IEmailProcessor>().ProcessAsync(claim, stoppingToken);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && stoppingToken.IsCancellationRequested))
                    {
                        await RecordFailureAsync(claim, ex, stoppingToken);
                    }
                }

                if (skipped > 0 && _logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning(
                        "{Skipped} claimed emails were skipped because too little of their lease was left to send them; raise Email:ClaimLeaseSeconds or lower Email:BatchSize",
                        skipped);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(ex, "Email drain cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_opts.PollSeconds), stoppingToken);
        }
    }

    // Message-level failures log the exception type only: the exception text can contain the recipient.
    private async Task RecordFailureAsync(EmailClaim claim, Exception ex, CancellationToken stoppingToken)
    {
        if (_logger.IsEnabled(LogLevel.Error))
            _logger.LogError("Email {MessageId} for tenant {TenantId} failed ({ExceptionType})",
                claim.Id, claim.TenantId, ex.GetType().Name);
        try
        {
            using var scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IEmailProcessor>().RecordFailureAsync(
                claim, $"Processing failed ({ex.GetType().Name}); see the worker log.", stoppingToken);
        }
        catch (Exception recordEx) when (!(recordEx is OperationCanceledException && stoppingToken.IsCancellationRequested))
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError(
                    "Could not record the failure of email {MessageId} for tenant {TenantId} ({ExceptionType}); the claim lease will return it",
                    claim.Id, claim.TenantId, recordEx.GetType().Name);
        }
    }
}
