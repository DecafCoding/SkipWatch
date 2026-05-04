using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkipWatch.Core.Db;
using SkipWatch.Core.Entities;
using SkipWatch.Core.Services.Transcripts;

namespace SkipWatch.Services.Workers;

public sealed class TranscriptWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TranscriptWorkerSettings _settings;
    private readonly ILogger<TranscriptWorker> _logger;

    public TranscriptWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<TranscriptWorkerSettings> settings,
        ILogger<TranscriptWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "TranscriptWorker starting. concurrency={Concurrency} idlePoll={IdlePollSeconds}s",
            _settings.Concurrency, _settings.IdlePollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool didWork;
            try
            {
                didWork = await TickOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TranscriptWorker tick threw");
                didWork = false;
            }

            if (!didWork)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.IdlePollSeconds), stoppingToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("TranscriptWorker stopping.");
    }

    // Returns true if a row was processed (so the loop should immediately try the next),
    // false if the queue was empty (so the loop should sleep before re-querying).
    private async Task<bool> TickOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SkipWatchDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<ITranscriptIngestRunner>();

        var now = DateTime.UtcNow;
        var video = await db.Videos
            .Where(v => v.Status == VideoStatus.Discovered
                && !v.Parked
                && (v.NextAttemptAt == null || v.NextAttemptAt <= now))
            .OrderBy(v => v.NextAttemptAt)
            .ThenBy(v => v.IngestedAt)
            .FirstOrDefaultAsync(ct);

        if (video is null) return false;

        await runner.RunAsync(video, ct);
        return true;
    }
}
