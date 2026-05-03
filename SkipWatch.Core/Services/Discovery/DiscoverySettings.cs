namespace SkipWatch.Core.Services.Discovery;

public sealed class DiscoverySettings
{
    public string Cron { get; set; } = "*/30 * * * *";
    public int ChannelsPerRound { get; set; } = 5;
    public int InitialVideoCap { get; set; } = 20;
    public int RollingVideoCap { get; set; } = 10;
    public int MinVideoDurationSeconds { get; set; } = 180;
    public int MaxVideoDurationMinutes { get; set; } = 60;
    public int MaxRetryAttempts { get; set; } = 3;
}
