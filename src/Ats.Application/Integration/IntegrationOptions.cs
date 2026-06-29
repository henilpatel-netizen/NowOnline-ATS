namespace Ats.Application.Integration;

public sealed class IntegrationOptions
{
    public int PollSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 100;
    public int MaxAttempts { get; set; } = 48;
    public int BaseBackoffSeconds { get; set; } = 30;
    public int MaxBackoffSeconds { get; set; } = 1800;
}
