using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkipWatch.Core.Db;
using SkipWatch.Core.Services.Discovery;

namespace SkipWatch.Services.Discovery;

public sealed class CollectionRoundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscoverySettings _settings;
    private readonly ILogger<CollectionRoundService> _logger;
    private readonly CronSchedule _schedule;

    public CollectionRoundService(
        IServiceScopeFactory scopeFactory,
        IOptions<DiscoverySettings> settings,
        ILogger<CollectionRoundService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
        _schedule = CronSchedule.Parse(_settings.Cron);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CollectionRoundService starting. Schedule: {Cron} (fixedInterval={Fixed})",
            _schedule.Expression, _schedule.FixedInterval);

        if (_schedule.FixedInterval is { } interval)
        {
            using var timer = new PeriodicTimer(interval);
            do { await SafeRunRoundAsync(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await SafeRunRoundAsync(stoppingToken);
            var delay = _schedule.GetDelayFromUtcNow(DateTime.UtcNow);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SafeRunRoundAsync(CancellationToken ct)
    {
        try
        {
            await RunRoundAsync(ct);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discovery round threw");
        }
    }

    private async Task RunRoundAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var cutoff = startedAt.AddHours(-24);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SkipWatchDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<IChannelDiscoveryRunner>();

        var channels = await db.Channels
            .Where(c => c.Enabled && (c.LastCheckAt == null || c.LastCheckAt < cutoff))
            .OrderBy(c => c.LastCheckAt == null ? 0 : 1)
            .ThenBy(c => c.LastCheckAt)
            .Take(_settings.ChannelsPerRound)
            .ToListAsync(ct);

        _logger.LogInformation("Discovery round starting. Picked {Count} channel(s).", channels.Count);

        foreach (var channel in channels)
        {
            if (ct.IsCancellationRequested) break;
            ChannelDiscoveryResult? result = null;
            string? error = null;
            try
            {
                result = await runner.RunAsync(channel, ct);
                error = result.Error;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogError(ex, "Discovery failed for channel {ChannelId} ({Title})",
                    channel.YoutubeChannelId, channel.Title);
            }

            channel.LastCheckAt = DateTime.UtcNow;
            channel.LastCheckError = error;
            await db.SaveChangesAsync(ct);

            if (result is not null)
            {
                _logger.LogInformation(
                    "Channel {ChannelId} ({Title}): +{New} discovered, {Short} short, {Long} too long, quotaExceeded={QuotaExceeded}",
                    channel.YoutubeChannelId, channel.Title,
                    result.NewDiscovered, result.SkippedShort, result.SkippedTooLong, result.QuotaExceeded);
            }
        }

        _logger.LogInformation("Discovery round complete in {Elapsed}ms.",
            (int)(DateTime.UtcNow - startedAt).TotalMilliseconds);
    }
}
