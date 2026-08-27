namespace Ats.Application.Integration;

public sealed class IntegrationOptions
{
    public int PollSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 100;
    public int MaxAttempts { get; set; } = 48;
    public int BaseBackoffSeconds { get; set; } = 30;
    public int MaxBackoffSeconds { get; set; } = 1800;
    // Visibility-timeout for a claimed (Processing) message. If a worker crashes mid-delivery, the
    // message is reclaimed once this lease elapses. Must comfortably exceed a single delivery attempt.
    public int ClaimLeaseSeconds { get; set; } = 300;
}
