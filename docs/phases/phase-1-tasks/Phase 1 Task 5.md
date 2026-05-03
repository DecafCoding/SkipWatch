# Phase 1 — Task 5: CREATE `CollectionRoundService` (`BackgroundService`) in the host project

You are executing one task from SkipWatch Phase 1 (channel discovery round). The full phase plan is at [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md). This file is self-contained for executing Task 5.

## Prerequisites

Tasks 1-4 complete (`DiscoverySettings`, `CronSchedule`, the two new methods on `IYouTubeApiService`, `IChannelDiscoveryRunner` + `ChannelDiscoveryRunner`).

## Working directory

cwd = `c:/Repos/Personal/SkipWatch`.

## Phase context (why this task exists)

The hosted service owns scheduling and the per-round channel-selection query. It runs as a singleton (`AddHostedService<T>` registers it that way), so it must use `IServiceScopeFactory` to resolve the scoped `SkipWatchDbContext` and scoped `IChannelDiscoveryRunner` per round. The per-channel `LastCheckAt` / `LastCheckError` update lives here — *not* in the runner — so the column is updated even when the runner throws, keeping rotation moving on the next start.

PRD §6 Phase 1 channel-selection rule:
- `Enabled = true AND (LastCheckAt IS NULL OR LastCheckAt < UtcNow - 24h)`
- Order: never-checked first, then oldest-checked
- Take `ChannelsPerRound`

The `*/N` cron form runs the round **immediately on startup** and then every N minutes (do/while loop).

## Files you MUST read before implementing

- [SkipWatch/Program.cs](../../../SkipWatch/SkipWatch/Program.cs) — lines 50-54 already use `app.Services.CreateScope()` to resolve a scoped `SkipWatchDbContext`. Same pattern, but inside a hosted service via `IServiceScopeFactory`.
- [SkipWatch.Core/Db/SkipWatchDbContext.cs](../../../SkipWatch/SkipWatch.Core/Db/SkipWatchDbContext.cs) — `Channels` DbSet and `idx_channels_round_pick` index the selection query is built around.
- [SkipWatch.Core/Entities/Channel.cs](../../../SkipWatch/SkipWatch.Core/Entities/Channel.cs) — fields the orchestrator writes (`LastCheckAt`, `LastCheckError`).
- `SkipWatch.Core/Services/Discovery/CronSchedule.cs` (Task 2) — `Parse`, `FixedInterval`, `GetDelayFromUtcNow`.
- `SkipWatch.Core/Services/Discovery/IChannelDiscoveryRunner.cs` (Task 4) — interface + `ChannelDiscoveryResult`.

## Reference docs

- .NET `BackgroundService` — `ExecuteAsync(CancellationToken stoppingToken)` pattern.
- `PeriodicTimer` — `await timer.WaitForNextTickAsync(stoppingToken)` returns false on cancellation.
- EF Core scoped service from singleton — resolve via `IServiceScopeFactory.CreateScope()` per round.

## The task

### IMPLEMENT

Create `SkipWatch/Services/Discovery/CollectionRoundService.cs` (the `SkipWatch/Services/` folder already exists):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkipWatch.Core.Db;
using SkipWatch.Core.Entities;
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
```

### PATTERN

`Program.cs` lines 50-54 already use `app.Services.CreateScope()` to resolve a scoped `SkipWatchDbContext` from the singleton `IServiceProvider`.

### GOTCHAS

- `Channels.OrderBy(c => c.LastCheckAt)` puts NULLs first by default in SQLite (NULLS FIRST is SQLite's default for ASC). The PRD's `ORDER BY last_check_at IS NULL DESC, last_check_at ASC` is achieved with the `OrderBy(c => c.LastCheckAt == null ? 0 : 1).ThenBy(c => c.LastCheckAt)` form — the explicit `0/1` form makes the intent obvious in C# and is portable across providers.
- The `LastCheckAt` update + `SaveChangesAsync` runs **per channel**, not once at the end of the round. A hard process kill mid-round still records progress for already-visited channels.
- The `*/N` shortcut runs the round **immediately on startup** (the `do { ... } while (...)` shape) rather than waiting one full interval. This matches the operational expectation: starting the host should produce a round in the logs within seconds.

### VALIDATE

```bash
cd c:/Repos/Personal/SkipWatch
dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
  && grep -q 'class CollectionRoundService' SkipWatch/Services/Discovery/CollectionRoundService.cs
```

Exit code must be 0.
