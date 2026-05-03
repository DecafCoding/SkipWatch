# Phase 1: Discovery round

The following plan should be complete, but it is important that you validate documentation and codebase patterns and task sanity before you start implementing.

Pay special attention to naming of existing utils, types, and models. Import from the right files.

## Phase Description

Stand up the channel discovery loop described in PRD §6 Phase 1: a single hosted service (`CollectionRoundService`) that wakes on a configurable schedule, picks up to `CHANNELS_PER_ROUND` enabled channels not visited in the last 24 hours, and runs the two-call YouTube Data API harvest per channel (`playlistItems.list` against the channel's uploads playlist, `videos.list` for `contentDetails.duration` + `statistics`). Each new video lands in the `videos` table with `Status = Discovered` (or `SkippedShort` / `SkippedTooLong` after the duration gate). Inserting a `Discovered` row is the entire enqueue mechanism for the Phase 2 transcript worker — there is no separate queue.

This phase scopes to **channel** discovery only. Topic discovery is PRD Phase 1b and lands in its own phase doc later. Apify, the LLM, and the triage UI all stay untouched. The `/debug/yt/channel/{handleOrId}` endpoint added during Phase 0 scaffolding is removed at the end of this phase per PRD §6 ("removed once their respective workers land") since the round itself is now the production caller of `GetChannelInfoAsync`'s sibling endpoints.

The discovery round is intentionally cheap: 5 channels × 2 calls × 48 rounds/day = **480 quota units/day**, under 5% of the 10k/day default. Adding more followed channels does not raise the burn — the round size is fixed, not the channel list size.

## User Stories

As a SkipWatch user
I want SkipWatch to poll my followed channels on a fixed schedule and surface their new uploads to the pipeline
So that I never have to open YouTube to discover what dropped today.

As a SkipWatch user
I want videos shorter than 3 minutes or longer than 60 minutes to be filtered out before transcription is attempted
So that Apify and LLM spend stays focused on the kind of content I actually triage.

As a SkipWatch operator
I want the round to honor a cron schedule and a soft quota ceiling
So that I can tune burn without redeploying and so a runaway loop can't blow the daily YouTube quota.

## Problem Statement

The Phase 0 skeleton has the channels table, the `IYouTubeApiService.GetChannelInfoAsync` resolver, the quota manager, and the channel-add UI — but nothing actually polls. Every video in the system today arrived via the manual `/debug/yt/channel/{handleOrId}` smoke endpoint, which only resolves the channel and never reads its uploads playlist. Without the discovery loop:

- Phase 2's transcript worker has nothing to drain (its query is `WHERE Status = 'Discovered'`).
- The 24-hour fairness gate on `last_check_at` is unenforceable because nothing writes that column.
- The duration filter (skip < 3 min, skip > 60 min) lives only in the PRD.
- The cron schedule, channel cap, and result caps that gate quota burn live only in the PRD.

## Solution Statement

Add the four discrete pieces required to make discovery run end-to-end:

1. **`DiscoveryRoundSettings`** typed-options record bound to the `Discovery` configuration section, holding the seven Phase-1 constants (`Cron`, `ChannelsPerRound`, `InitialVideoCap`, `RollingVideoCap`, `MinVideoDurationSeconds`, `MaxVideoDurationMinutes`, `MaxRetryAttempts`).
2. **YouTube API surface extension** — two new methods on `IYouTubeApiService` for the round itself (`ListUploadsPageAsync`, `GetVideoDetailsAsync`), implemented on top of the existing `Google.Apis.YouTube.v3.YouTubeService` client and gated by `IYouTubeQuotaManager`. The 1-unit-each cost model is already encoded in `YouTubeApiOperationCosts` (`GetPlaylistItems = 1`, `GetVideoDetails = 1`).
3. **`ChannelDiscoveryRunner`** — pure per-channel logic: fetch uploads page, intersect against existing `Videos.YoutubeVideoId` rows to short-circuit, enrich the new IDs with `GetVideoDetailsAsync`, apply the duration gate, insert rows, and update `LastCheckAt` / `LastCheckError`. Lives in `SkipWatch.Core` so tests can exercise it without booting the host.
4. **`CollectionRoundService`** — the `BackgroundService` that owns scheduling. Parses `Discovery:Cron` with NCrontab; if the value matches the simple `*/N * * * *` form, short-circuits to `PeriodicTimer(TimeSpan.FromMinutes(N))` per the PRD. On each tick: select up to `ChannelsPerRound` channels by the SQL in PRD §6 Phase 1, hand each one to `ChannelDiscoveryRunner` inside its own DI scope, log the round summary.

Tests: a fake `IYouTubeApiService` exercises `ChannelDiscoveryRunner` against an in-memory SQLite DbContext and asserts the row counts / statuses; a small unit test pins the `*/30 * * * *` shortcut and a non-shortcut cron expression.

## Phase Metadata

**Phase Type**: New Capability
**Estimated Complexity**: Medium
**Primary Systems Affected**: `SkipWatch.Core/Services/Discovery/` (new), `SkipWatch.Core/Services/YouTube/` (extension), `SkipWatch/Program.cs` (DI + endpoint removal), `SkipWatch/appsettings.json` (new `Discovery` section), `SkipWatch.Tests/` (new fixtures)
**Dependencies**: Phase 0 complete (test project, EF model, YouTube client, quota manager). New NuGet: `NCrontab` (Core project). No new external services.

---

## CONTEXT REFERENCES

### Relevant Codebase Files IMPORTANT: YOU MUST READ THESE FILES BEFORE IMPLEMENTING!

- [SkipWatch.Core/Services/YouTube/YouTubeApiService.cs](../../SkipWatch/SkipWatch.Core/Services/YouTube/YouTubeApiService.cs) — current `GetChannelInfoAsync` shape; mirror its quota gating (`TryConsumeQuotaAsync`), error wrapping (`GoogleApiException` → `IsQuotaExceeded` flag), and `_youTubeClient` reuse for the two new methods. The client is constructed once in `.ctor` and disposed via `IDisposable`.
- [SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs](../../SkipWatch/SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs) — interface to extend; the existing `ChannelInfoResult` record sits in this same file. New result records (`UploadsPageResult`, `VideoDetailsResult`) belong here too.
- [SkipWatch.Core/Services/Interfaces/IYouTubeQuotaManager.cs](../../SkipWatch/SkipWatch.Core/Services/Interfaces/IYouTubeQuotaManager.cs) — note the `TryConsumeQuotaAsync(YouTubeApiOperation, int requestCount = 1)` signature. Pass `requestCount = 1` per call (each `playlistItems.list` page is 1 unit; each `videos.list` page of up to 50 IDs is 1 unit).
- [SkipWatch.Core/Services/YouTube/Models/YouTubeApiSettings.cs](../../SkipWatch/SkipWatch.Core/Services/YouTube/Models/YouTubeApiSettings.cs) — `YouTubeApiOperation.GetPlaylistItems` (cost 1) and `GetVideoDetails` (cost 1) are already defined; do **not** add new operation enum values.
- [SkipWatch.Core/Entities/Channel.cs](../../SkipWatch/SkipWatch.Core/Entities/Channel.cs) — fields the runner reads (`UploadsPlaylistId`, `Enabled`, `LastCheckAt`) and writes (`LastCheckAt`, `LastCheckError`).
- [SkipWatch.Core/Entities/Video.cs](../../SkipWatch/SkipWatch.Core/Entities/Video.cs) — fields the runner writes on insert. Note `IngestedAt` defaults to `DateTime.UtcNow`. Note enum names: `VideoStatus.Discovered`, `VideoStatus.SkippedShort`, `VideoStatus.SkippedTooLong` (NOT `Skipped_Short`).
- [SkipWatch.Core/Db/SkipWatchDbContext.cs](../../SkipWatch/SkipWatch.Core/Db/SkipWatchDbContext.cs) — `idx_channels_round_pick` (lines 29-30) is the index the channel-selection query is built around. `idx_videos_q_transcript` (lines 53-56) is what Phase 2 will read; we just need to make sure newly-inserted rows satisfy `Status = 'Discovered' AND Parked = 0` so they appear in it.
- [SkipWatch/Program.cs](../../SkipWatch/SkipWatch/Program.cs) — DI registrations live here; lines 33-46 are the registration block. The `/debug/yt/channel/{handleOrId}` endpoint at lines 79-107 is removed in this phase. Note that `AddDbContext<SkipWatchDbContext>` is registered with default scoped lifetime — `CollectionRoundService` must use `IServiceScopeFactory` to resolve the DbContext per tick.
- [SkipWatch/Features/Channels/Services/ChannelService.cs](../../SkipWatch/SkipWatch/Features/Channels/Services/ChannelService.cs) — pattern for `IYouTubeApiService` consumption (constructor injection, `ChannelInfoResult.Success` branching, EF Core `SaveChangesAsync` with cancellation token).
- [SkipWatch.Core/Services/YouTube/DurationParser.cs](../../SkipWatch/SkipWatch.Core/Services/YouTube/DurationParser.cs) — already in the repo; check whether it parses ISO-8601 (`PT4M13S`) durations to `TimeSpan` or seconds. Reuse it; do not write a second parser.
- [SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs](../../SkipWatch/SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs) — in-memory SQLite fixture pattern (`SqliteConnection("Data Source=:memory:")` kept open for the test lifetime, `Database.Migrate()` to apply migrations). Mirror exactly for the runner fixture.
- [docs/prd.md §6 Phase 1](../prd.md) — single source of truth for round semantics, the channel-selection SQL, the duration gate, and the constant defaults. **Read before coding.** Note the explicit caveat that Phase 1b (topics) is *not* in this phase.

### New Files to Create

- `SkipWatch.Core/Services/Discovery/DiscoverySettings.cs` — typed options record bound to the `Discovery` config section.
- `SkipWatch.Core/Services/Discovery/CronSchedule.cs` — small helper that wraps NCrontab with the `*/N * * * *` → `PeriodicTimer(N min)` shortcut documented in the PRD.
- `SkipWatch.Core/Services/Discovery/IChannelDiscoveryRunner.cs` — interface so tests can swap implementations.
- `SkipWatch.Core/Services/Discovery/ChannelDiscoveryRunner.cs` — per-channel logic.
- `SkipWatch/Services/Discovery/CollectionRoundService.cs` — `BackgroundService` (lives in the host project, not Core, because it depends on `IServiceScopeFactory` and is a hosted service). Folder `SkipWatch/Services/` already exists.
- `SkipWatch.Tests/Services/Discovery/CronScheduleTests.cs` — schedule-parser unit tests.
- `SkipWatch.Tests/Services/Discovery/ChannelDiscoveryRunnerTests.cs` — runner tests with a fake `IYouTubeApiService` and an in-memory SQLite DbContext.

### Modified Files

- `SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs` — add two methods + their result records.
- `SkipWatch.Core/Services/YouTube/YouTubeApiService.cs` — implement the two new methods.
- `SkipWatch.Core/SkipWatch.Core.csproj` — add `NCrontab` package reference.
- `SkipWatch/Program.cs` — register `DiscoverySettings`, `IChannelDiscoveryRunner`, `CollectionRoundService` (`AddHostedService`); remove the `/debug/yt/channel/{handleOrId}` endpoint.
- `SkipWatch/appsettings.json` — add the `Discovery` section with the seven defaults.

### Relevant Documentation YOU SHOULD READ THESE BEFORE IMPLEMENTING!

- [YouTube Data API v3 — `playlistItems.list`](https://developers.google.com/youtube/v3/docs/playlistItems/list)
  - `playlistId` parameter, `part=snippet,contentDetails`, `maxResults` (max 50), `pageToken` for paging.
  - Why: this is the call against the channel's uploads playlist that drives the entire round.
- [YouTube Data API v3 — `videos.list`](https://developers.google.com/youtube/v3/docs/videos/list)
  - `id` parameter (comma-separated, up to 50), `part=contentDetails,statistics` for `duration` + `viewCount`/`likeCount`/`commentCount`.
  - Why: enrichment call that produces the duration used by the gate. One unit per page of 50.
- [`Google.Apis.YouTube.v3` — `PlaylistItemsResource.ListRequest`](https://googleapis.dev/dotnet/Google.Apis.YouTube.v3/latest/api/Google.Apis.YouTube.v3.PlaylistItemsResource.ListRequest.html) and [`VideosResource.ListRequest`](https://googleapis.dev/dotnet/Google.Apis.YouTube.v3/latest/api/Google.Apis.YouTube.v3.VideosResource.ListRequest.html)
  - The strongly-typed wrappers `_youTubeClient.PlaylistItems.List(...)` and `_youTubeClient.Videos.List(...)`. Same shape as `_youTubeClient.Channels.List(...)` already used in `YouTubeApiService.GetChannelInfoAsync`.
- [.NET `BackgroundService`](https://learn.microsoft.com/dotnet/core/extensions/workers)
  - Pattern for `ExecuteAsync(CancellationToken stoppingToken)`. The PRD round runs to completion in seconds, so the loop is `while (!stoppingToken.IsCancellationRequested) { await WaitForNextTick(...); await RunRoundAsync(...); }`.
- [`PeriodicTimer`](https://learn.microsoft.com/dotnet/api/system.threading.periodictimer)
  - Used for the `*/N` shortcut. `await timer.WaitForNextTickAsync(stoppingToken)` returns false on cancellation.
- [NCrontab](https://github.com/atifaziz/NCrontab) — current 5-field cron format. `CrontabSchedule.Parse(expr).GetNextOccurrence(DateTime.UtcNow)` is the call. Used only for the non-shortcut path.
- [EF Core `IServiceScopeFactory`](https://learn.microsoft.com/dotnet/core/extensions/scoped-service) — pattern for resolving a scoped DbContext from a singleton hosted service. The `CollectionRoundService` is a hosted service (singleton lifetime); each round opens a fresh scope and resolves `SkipWatchDbContext` from it.

### Patterns to Follow

**Naming Conventions:**
- Folders match feature surface: `Services/Discovery/` mirrors the existing `Services/YouTube/` and `Services/Transcripts/`.
- Interfaces prefixed `I` (e.g., `IChannelDiscoveryRunner`).
- Result records sit beside the interface that returns them (PRD-established convention; see `ChannelInfoResult` in `IYouTubeApiService.cs`).

**Quota gating:**
Mirror `YouTubeApiService.GetChannelInfoAsync` lines 50-52 — call `TryConsumeQuotaAsync(operation)` *before* calling the API; if it returns `false`, return a result record with `IsQuotaExceeded = true` and a non-success state. Do **not** use the reservation API (`ReserveQuotaAsync`/`ConfirmReservationAsync`) — for fixed-cost calls the consume-then-call shape is what the existing code uses, and the round itself is bounded so optimistic consumption can't burn through the ceiling.

**DbContext usage in a hosted service:**
The hosted service is a singleton; `SkipWatchDbContext` is scoped. Pattern (mirrors lines 50-54 in `Program.cs` where `Database.Migrate()` is called at startup):
```csharp
using var scope = _scopeFactory.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<SkipWatchDbContext>();
var runner = scope.ServiceProvider.GetRequiredService<IChannelDiscoveryRunner>();
// ... do work, scope dispose flushes the change tracker
```

**Logging:**
`ILogger<T>` with structured templates and PascalCase parameter names — matches `YouTubeApiService` (e.g., `_logger.LogInformation("Quota consumed: {Operation} x{RequestCount} ...", ...)`). The round emits at minimum: round start with channel count, per-channel result (channel ID + new video count + skipped count), round end with elapsed milliseconds.

**Error Handling:**
Per-channel errors must not poison the round. Wrap each channel iteration in `try`/`catch (Exception)`, log the error, write the message to `Channel.LastCheckError`, **still update `LastCheckAt`** (per PRD: "updated whether the visit succeeds or fails so a broken channel can't block rotation"), and continue to the next channel.

**Time:**
Use `DateTime.UtcNow` everywhere. The selection query expresses the 24-hour gate against UTC; SQLite's `datetime('now', '-24 hours')` is UTC. EF Core's parameterization via LINQ-to-SQL handles the comparison.

---

## IMPLEMENTATION PLAN

**Rendering**: Flat

**Rationale**: 8 implementation tasks plus the mandatory final commit/push/PR task. The work has natural layering (config → API extension → runner → service → DI/wiring → tests) but each task is independently validatable and the chain is short enough that milestone wrappers would add noise without organizational benefit. The runner and the hosted service are deliberately split — the runner is pure logic in `SkipWatch.Core` and tests against an in-memory DbContext; the service is the scheduling shell in the host project. The final commit/push/PR task is rendered as a single-task milestone per the template's mandate.

Tasks always execute one at a time, top to bottom. Each task carries its own VALIDATE that runs the moment that task completes.

**Working directory note:** all task validation commands assume the executor is at `c:/Repos/Personal/SkipWatch` (the git repo root) unless explicitly noted.

#### Task 1: CREATE `DiscoverySettings` and bind it in DI

Add the typed-options record holding the seven Phase-1 constants and bind it to a new `Discovery` section in `appsettings.json`.

- **IMPLEMENT**:
  1. Create `SkipWatch.Core/Services/Discovery/DiscoverySettings.cs`:
     ```csharp
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
     ```
  2. In `SkipWatch/appsettings.json`, add a `"Discovery"` block alongside `"YouTube"` and `"Apify"` with the same default values listed above (all keys present, even though `Configure<DiscoverySettings>` would fall back to the defaults — explicit defaults make the file self-documenting).
  3. In `SkipWatch/Program.cs`, in the configuration block (after line 31 `Configure<ApifySettings>`), add:
     ```csharp
     builder.Services.Configure<DiscoverySettings>(builder.Configuration.GetSection("Discovery"));
     ```
     and add the corresponding `using SkipWatch.Core.Services.Discovery;` at the top.
- **PATTERN**: `YouTubeApiSettings` and `ApifySettings` (already in `Program.cs` lines 30-31).
- **IMPORTS**: New `using SkipWatch.Core.Services.Discovery;` in `Program.cs`.
- **GOTCHA**: The PRD spells the cron knob as the env-var name `SKIPWATCH_ROUND_CRON`. ASP.NET Core's environment-variable provider already remaps double-underscored env vars to colon-separated config keys (`Discovery__Cron` → `Discovery:Cron`). Operators who prefer the PRD's name set `Discovery__Cron` rather than `SKIPWATCH_ROUND_CRON`. Documenting this in the PR body is sufficient; do not introduce a custom env-var alias.
- **VALIDATE**: `dotnet build SkipWatch.slnx -c Debug --nologo -v quiet` exits 0, and the binding is exercised inline:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
    && grep -q '"Discovery"' SkipWatch/appsettings.json \
    && grep -q 'Configure<DiscoverySettings>' SkipWatch/Program.cs
  ```

#### Task 2: ADD `NCrontab` package + create `CronSchedule` helper

The PRD requires NCrontab parsing for general expressions and a `*/N * * * *` short-circuit to `PeriodicTimer`. Wrap both behind a single `CronSchedule` class.

- **IMPLEMENT**:
  1. From `c:/Repos/Personal/SkipWatch`, run `dotnet add SkipWatch.Core/SkipWatch.Core.csproj package NCrontab` (latest 3.x; pin the version the CLI selects).
  2. Create `SkipWatch.Core/Services/Discovery/CronSchedule.cs`:
     ```csharp
     using System.Text.RegularExpressions;
     using NCrontab;

     namespace SkipWatch.Core.Services.Discovery;

     /// <summary>
     /// Wraps an NCrontab schedule with a `*/N * * * *` shortcut that uses PeriodicTimer
     /// directly. The shortcut avoids drift introduced by recomputing GetNextOccurrence
     /// on every tick for the common case documented in PRD §6 Phase 1.
     /// </summary>
     public sealed class CronSchedule
     {
         private static readonly Regex EveryNMinutes = new(
             @"^\*/(\d+)\s+\*\s+\*\s+\*\s+\*$", RegexOptions.Compiled);

         private readonly CrontabSchedule? _schedule;
         public TimeSpan? FixedInterval { get; }
         public string Expression { get; }

         private CronSchedule(string expression, CrontabSchedule? schedule, TimeSpan? fixedInterval)
         {
             Expression = expression;
             _schedule = schedule;
             FixedInterval = fixedInterval;
         }

         public static CronSchedule Parse(string expression)
         {
             ArgumentException.ThrowIfNullOrWhiteSpace(expression);
             var match = EveryNMinutes.Match(expression.Trim());
             if (match.Success && int.TryParse(match.Groups[1].Value, out var minutes) && minutes > 0)
                 return new CronSchedule(expression, schedule: null, TimeSpan.FromMinutes(minutes));
             var schedule = CrontabSchedule.Parse(expression);
             return new CronSchedule(expression, schedule, fixedInterval: null);
         }

         public TimeSpan GetDelayFromUtcNow(DateTime utcNow)
         {
             if (FixedInterval is { } interval)
                 return interval;
             var next = _schedule!.GetNextOccurrence(utcNow);
             var delay = next - utcNow;
             return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
         }
     }
     ```
- **PATTERN**: Static `Parse` factory + sealed type, mirroring `Transcript`/`ChannelInfoResult` immutability conventions.
- **IMPORTS**: `NCrontab` (from the new package), `System.Text.RegularExpressions`.
- **GOTCHA**: NCrontab's default constructor accepts only the standard 5-field format. Do not pass the 6-field (with-seconds) form; the PRD examples are 5-field.
- **GOTCHA**: `FixedInterval` exists so the hosted service can hand the value directly to `new PeriodicTimer(interval)` — Task 5 reads this property to decide which scheduling path to take.
- **VALIDATE**:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
    && grep -q 'NCrontab' SkipWatch.Core/SkipWatch.Core.csproj
  ```

#### Task 3: EXTEND `IYouTubeApiService` with `ListUploadsPageAsync` and `GetVideoDetailsAsync`

Add the two methods that the round itself calls. Each costs 1 quota unit per request and returns a result record alongside `ChannelInfoResult` in `IYouTubeApiService.cs`.

- **IMPLEMENT**:
  1. In `SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs`, add to the interface:
     ```csharp
     /// <summary>
     /// One page of a channel's uploads playlist. Costs 1 quota unit per call.
     /// Caller controls paging via <paramref name="pageToken"/>.
     /// </summary>
     Task<UploadsPageResult> ListUploadsPageAsync(
         string uploadsPlaylistId,
         int maxResults,
         string? pageToken,
         CancellationToken ct = default);

     /// <summary>
     /// videos.list enrichment for up to 50 IDs in a single call.
     /// Costs 1 quota unit per call regardless of ID count.
     /// </summary>
     Task<VideoDetailsResult> GetVideoDetailsAsync(
         IReadOnlyCollection<string> videoIds,
         CancellationToken ct = default);
     ```
     and the result records:
     ```csharp
     public sealed record UploadsPageResult(
         bool Success,
         IReadOnlyList<UploadsPageItem> Items,
         string? NextPageToken,
         string? ErrorMessage,
         bool IsQuotaExceeded);

     public sealed record UploadsPageItem(
         string YoutubeVideoId,
         string Title,
         DateTime PublishedAt,
         string? ThumbnailUrl);

     public sealed record VideoDetailsResult(
         bool Success,
         IReadOnlyList<VideoDetails> Items,
         string? ErrorMessage,
         bool IsQuotaExceeded);

     public sealed record VideoDetails(
         string YoutubeVideoId,
         int? DurationSeconds,
         long? ViewCount,
         long? LikeCount,
         long? CommentsCount);
     ```
  2. In `SkipWatch.Core/Services/YouTube/YouTubeApiService.cs`, implement both methods. Mirror `GetChannelInfoAsync` for quota gating and exception wrapping. Use:
     - `_youTubeClient.PlaylistItems.List("snippet,contentDetails")` with `PlaylistId = uploadsPlaylistId`, `MaxResults = maxResults`, `PageToken = pageToken`. Map each `Item` to `UploadsPageItem` (`YoutubeVideoId = item.ContentDetails.VideoId`, `Title = item.Snippet.Title`, `PublishedAt = item.ContentDetails.VideoPublishedAt?.DateTime ?? item.Snippet.PublishedAt?.DateTime ?? DateTime.UtcNow`, `ThumbnailUrl = item.Snippet?.Thumbnails?.Default__?.Url ?? Medium ?? High`).
     - `_youTubeClient.Videos.List("contentDetails,statistics")` with `Id = string.Join(",", videoIds)`. Use `DurationParser.Parse(item.ContentDetails?.Duration)` to convert ISO-8601 → seconds (read `DurationParser.cs` first to confirm its public surface and return type; if it returns `TimeSpan?`, call `.TotalSeconds` and round). Stats are `ulong?` in the v3 client — cast to `long?` (any value over `long.MaxValue` is implausible for a YouTube stat).
- **PATTERN**: `GetChannelInfoAsync` lines 45-91 — same `TryConsumeQuotaAsync` → `try`/`catch (GoogleApiException)` shape. Reuse the existing `_youTubeClient`.
- **IMPORTS**: Existing `using Google.Apis.YouTube.v3;` covers the resource types.
- **GOTCHA**: The `videos.list` call accepts up to 50 IDs per request and is **always 1 unit per call** regardless of how many IDs you pass. Do not split a 30-ID batch into 30 calls.
- **GOTCHA**: When `videoIds` is empty, return `Success = true` with an empty `Items` list **without** consuming a quota unit. The runner can pre-filter to the empty case after intersecting with the DB.
- **GOTCHA**: `DurationParser` is the only pre-existing duration utility — confirm its name and signature with `Read` before importing. If it does not exist or has a different shape, *use it as-is* and do not introduce a parallel parser; if it is genuinely missing, write the parsing inline rather than creating a new file.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet build SkipWatch.slnx -c Debug --nologo -v quiet` exits 0 and the new symbols are present:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
    && grep -q 'ListUploadsPageAsync' SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs \
    && grep -q 'GetVideoDetailsAsync' SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs \
    && grep -q 'ListUploadsPageAsync' SkipWatch.Core/Services/YouTube/YouTubeApiService.cs \
    && grep -q 'GetVideoDetailsAsync' SkipWatch.Core/Services/YouTube/YouTubeApiService.cs
  ```

#### Task 4: CREATE `IChannelDiscoveryRunner` + `ChannelDiscoveryRunner`

Per-channel logic: page uploads, short-circuit on existing IDs, enrich, apply duration gate, insert. Pure orchestration over `IYouTubeApiService` and `SkipWatchDbContext` so the unit tests in Task 7 can fake both.

- **IMPLEMENT**:
  1. Create `SkipWatch.Core/Services/Discovery/IChannelDiscoveryRunner.cs`:
     ```csharp
     using SkipWatch.Core.Entities;

     namespace SkipWatch.Core.Services.Discovery;

     public interface IChannelDiscoveryRunner
     {
         Task<ChannelDiscoveryResult> RunAsync(Channel channel, CancellationToken ct = default);
     }

     public sealed record ChannelDiscoveryResult(
         int NewDiscovered,
         int SkippedShort,
         int SkippedTooLong,
         bool QuotaExceeded,
         string? Error);
     ```
  2. Create `SkipWatch.Core/Services/Discovery/ChannelDiscoveryRunner.cs`:
     - Constructor injects `SkipWatchDbContext db`, `IYouTubeApiService yt`, `IOptions<DiscoverySettings> settings`, `ILogger<ChannelDiscoveryRunner> logger`.
     - In `RunAsync`:
       1. Determine `cap`: `settings.Value.InitialVideoCap` if no Video rows exist for `channel.Id` (cold-start), else `settings.Value.RollingVideoCap`. Use `await _db.Videos.AnyAsync(v => v.ChannelId == channel.Id, ct)` to decide.
       2. Page `playlistItems.list` with `pageToken = null`, `maxResults = Math.Min(50, cap)`. Accumulate items into a list. **Stop paging the moment any returned `YoutubeVideoId` already exists in `_db.Videos`** (per PRD: "Stop paging the moment we hit a video already in the DB"). Also stop after `cap` items collected, after the first page if `NextPageToken` is null, or after a quota refusal (return early with `QuotaExceeded = true`).
       3. If the accumulated list is empty, return `new ChannelDiscoveryResult(0, 0, 0, false, null)`.
       4. Call `GetVideoDetailsAsync` once per batch of ≤ 50 (the typical case is a single batch). On quota refusal, return `QuotaExceeded = true`.
       5. Apply the duration gate to each item:
          - `duration <= MinVideoDurationSeconds` → `VideoStatus.SkippedShort`
          - `duration is null || duration > MaxVideoDurationMinutes * 60` → `VideoStatus.SkippedTooLong`
          - else → `VideoStatus.Discovered`
       6. Insert one `Video` row per item using the upload page item for `Title` / `PublishedAt` / `ThumbnailUrl` and the details record for `DurationSeconds` / `ViewCount` / `LikeCount` / `CommentsCount`. Set `ChannelId = channel.Id`. `IngestedAt`, `Status`, `RetryCount` defaults are already correct on the entity.
       7. `await _db.SaveChangesAsync(ct)` once at the end of the channel.
       8. Return the tally.
     - The runner does **not** update `channel.LastCheckAt` itself — the orchestrating `CollectionRoundService` does that (so the column is updated even when the runner throws).
- **PATTERN**: `ChannelService.AddAsync` for the EF Core pattern (`FirstOrDefaultAsync`, `Add`, single `SaveChangesAsync` per logical operation, cancellation token propagation).
- **IMPORTS**: `using Microsoft.EntityFrameworkCore;`, `using Microsoft.Extensions.Logging;`, `using Microsoft.Extensions.Options;`, `using SkipWatch.Core.Db;`, `using SkipWatch.Core.Entities;`, `using SkipWatch.Core.Services.Interfaces;`.
- **GOTCHA**: The "stop paging when an existing ID is hit" rule means *the first existing ID in batch order ends the round for this channel*, not "skip existing IDs and keep going". This matches the PRD's intent: uploads playlist returns newest first, so an existing ID means everything older is already in the DB.
- **GOTCHA**: When the duration gate marks a row `SkippedShort` or `SkippedTooLong`, those rows still get inserted (they are the audit trail for "we saw this and decided not to process it"). Phase 2's transcript worker filter (`WHERE Status = 'Discovered'`) skips them naturally.
- **GOTCHA**: Do not set `Video.ChannelId` to `0`. Channels coming in from `CollectionRoundService` are already-tracked entities loaded from the DB, so their `Id` is populated.
- **GOTCHA**: This task creates the `Discovery` folder under `SkipWatch.Core/Services/`. Tasks 1 and 2 already created files in it — confirm the folder exists rather than recreating it.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet build SkipWatch.slnx -c Debug --nologo -v quiet` exits 0 and:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
    && grep -q 'class ChannelDiscoveryRunner' SkipWatch.Core/Services/Discovery/ChannelDiscoveryRunner.cs \
    && grep -q 'IChannelDiscoveryRunner' SkipWatch.Core/Services/Discovery/IChannelDiscoveryRunner.cs
  ```

#### Task 5: CREATE `CollectionRoundService` (`BackgroundService`) in the host project

The hosted service that owns scheduling and the per-round channel-selection query.

- **IMPLEMENT**: Create `SkipWatch/Services/Discovery/CollectionRoundService.cs`:
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
- **PATTERN**: `Program.cs` lines 50-54 already use `app.Services.CreateScope()` to resolve a scoped `SkipWatchDbContext` from the singleton `IServiceProvider` — this is the same pattern, just inside a hosted service via `IServiceScopeFactory`.
- **IMPORTS**: As shown.
- **GOTCHA**: `Channels.OrderBy(c => c.LastCheckAt)` puts NULLs first by default in SQLite (NULLS FIRST is SQLite's default for ASC). The PRD's `ORDER BY last_check_at IS NULL DESC, last_check_at ASC` is achieved with the `OrderBy(c => c.LastCheckAt == null ? 0 : 1).ThenBy(c => c.LastCheckAt)` form above; both work, but the explicit `0/1` form makes the intent obvious in C# and is portable across providers.
- **GOTCHA**: The `LastCheckAt` update + `SaveChangesAsync` runs **per channel**, not once at the end of the round. This guarantees that a hard process kill mid-round still records progress for already-visited channels and keeps the rotation moving on the next start.
- **GOTCHA**: The `*/N` shortcut runs the round **immediately on startup** (the `do { ... } while (...)` shape) rather than waiting one full interval. This matches the operational expectation: starting the host should produce a round in the logs within seconds.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet build SkipWatch.slnx -c Debug --nologo -v quiet` exits 0 and:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
    && grep -q 'class CollectionRoundService' SkipWatch/Services/Discovery/CollectionRoundService.cs
  ```

#### Task 6: WIRE DI in `Program.cs` and REMOVE the `/debug/yt/channel` endpoint

Register the runner and the hosted service; delete the no-longer-needed debug endpoint.

- **IMPLEMENT**: In `SkipWatch/Program.cs`:
  1. After the existing `AddScoped` registrations for `IChannelService` / `ITopicService` (lines 40-43), add:
     ```csharp
     builder.Services.AddScoped<SkipWatch.Core.Services.Discovery.IChannelDiscoveryRunner,
         SkipWatch.Core.Services.Discovery.ChannelDiscoveryRunner>();
     builder.Services.AddHostedService<SkipWatch.Services.Discovery.CollectionRoundService>();
     ```
  2. Delete the entire `app.MapGet("/debug/yt/channel/{handleOrId}", ...)` block (currently lines 79-107 with the explanatory comment on lines 79-80). Leave the `/debug/transcript/{videoId}` block (Phase 2 owns its removal).
- **PATTERN**: Existing `AddScoped` and `AddSingleton` registrations in `Program.cs`.
- **IMPORTS**: No new top-level usings required (fully-qualified names used in the registrations to avoid disrupting the existing `using` ordering — `dotnet format` will normalize to whichever style `.editorconfig` enforces).
- **GOTCHA**: `AddHostedService<T>` registers `T` as a singleton automatically — do not also `AddSingleton`. The hosted service uses `IServiceScopeFactory` (always available without registration) to resolve scoped dependencies per round.
- **GOTCHA**: Removing the `/debug/yt/channel` endpoint also makes its `IYouTubeApiService` and `IYouTubeQuotaManager` lambda parameters disappear — this is fine, the services are still registered and still used by `ChannelService` and the new runner.
- **VALIDATE**:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
    && grep -q 'AddHostedService<SkipWatch.Services.Discovery.CollectionRoundService>' SkipWatch/Program.cs \
    && grep -q 'IChannelDiscoveryRunner' SkipWatch/Program.cs \
    && ! grep -q '/debug/yt/channel' SkipWatch/Program.cs
  ```

#### Task 7: CREATE `CronScheduleTests`

Pin the `*/30` shortcut and a non-shortcut expression so future refactors of `CronSchedule` cannot silently break the PRD-mandated behavior.

- **IMPLEMENT**: Create `SkipWatch.Tests/Services/Discovery/CronScheduleTests.cs`:
  ```csharp
  using SkipWatch.Core.Services.Discovery;

  namespace SkipWatch.Tests.Services.Discovery;

  public sealed class CronScheduleTests
  {
      [Theory]
      [InlineData("*/30 * * * *", 30)]
      [InlineData("*/5 * * * *", 5)]
      [InlineData("*/1 * * * *", 1)]
      public void EveryNMinutes_pattern_uses_periodic_timer_shortcut(string expr, int expectedMinutes)
      {
          var schedule = CronSchedule.Parse(expr);
          schedule.FixedInterval.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
      }

      [Theory]
      [InlineData("0 * * * *")]
      [InlineData("15 9 * * *")]
      [InlineData("*/30 9-17 * * *")]
      public void NonShortcut_expressions_use_ncrontab(string expr)
      {
          var schedule = CronSchedule.Parse(expr);
          schedule.FixedInterval.Should().BeNull();
          var anchor = new DateTime(2026, 5, 3, 12, 0, 0, DateTimeKind.Utc);
          schedule.GetDelayFromUtcNow(anchor).Should().BeGreaterThan(TimeSpan.Zero);
      }
  }
  ```
- **PATTERN**: `SkipWatchDbContextSmokeTests` for xUnit + FluentAssertions style. `Xunit` and `FluentAssertions` come from `SkipWatch.Tests/Usings.cs`.
- **IMPORTS**: As shown.
- **GOTCHA**: For the non-shortcut anchor `2026-05-03 12:00:00 UTC`, every NCrontab expression in the InlineData rows yields a future occurrence within 24 hours — so the delay is always positive. If a future change wants to test the "next occurrence is right now" boundary, use `BeGreaterThanOrEqualTo` instead.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj --filter "FullyQualifiedName~CronScheduleTests" --nologo -v quiet` exits 0 with 6 passing tests.

#### Task 8: CREATE `ChannelDiscoveryRunnerTests`

Cover the three behaviors that earn this phase its keep: cold-start cap, short-circuit on existing IDs, duration gate placing rows in the right status bucket.

- **IMPLEMENT**: Create `SkipWatch.Tests/Services/Discovery/ChannelDiscoveryRunnerTests.cs`:
  - Set up an in-memory SQLite DbContext exactly as `SkipWatchDbContextSmokeTests` does (open a `SqliteConnection("Data Source=:memory:")` for the test lifetime, call `Database.Migrate()`).
  - Define a `FakeYouTubeApi : IYouTubeApiService` test double with public lists for the queued `UploadsPageResult` returns and the `VideoDetailsResult` returns. `GetChannelInfoAsync` throws `NotImplementedException` (not exercised here).
  - Use `IOptions.Create(new DiscoverySettings { InitialVideoCap = 20, RollingVideoCap = 10, MinVideoDurationSeconds = 180, MaxVideoDurationMinutes = 60 })` for settings, and `NullLogger<ChannelDiscoveryRunner>.Instance`.
  - Three tests:
    1. `Cold_start_inserts_up_to_initial_cap`: queue an `UploadsPageResult` with 20 items (ids `v1`..`v20`, durations all 600s after enrichment) and assert 20 `Video` rows with `Status = Discovered` are written.
    2. `Stops_paging_when_existing_video_id_seen`: pre-insert one `Video` (`YoutubeVideoId = "v3"`) tied to the channel; queue an `UploadsPageResult` with `[v1, v2, v3, v4, v5]`. Assert only `v1` and `v2` are inserted (paging stops at `v3`); the enrichment call goes out for `[v1, v2]` only.
    3. `Duration_gate_places_rows_in_correct_status_buckets`: queue 3 items (`vshort` 60s, `vok` 600s, `vlong` 4000s) and assert exactly one row each of `SkippedShort`, `Discovered`, `SkippedTooLong`.
- **PATTERN**: `SkipWatchDbContextSmokeTests` for the in-memory SQLite fixture; production `IYouTubeApiService` consumers (`ChannelService`) for the result-record shape.
- **IMPORTS**: `Microsoft.Data.Sqlite`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `SkipWatch.Core.Db`, `SkipWatch.Core.Entities`, `SkipWatch.Core.Services.Discovery`, `SkipWatch.Core.Services.Interfaces`, `SkipWatch.Core.Services.YouTube.Models`.
- **GOTCHA**: Each test owns its own `SqliteConnection` and DbContext; do not share via class fixture. The connection must remain open for the full test (closing flushes the in-memory DB).
- **GOTCHA**: The `Channel` you seed must have a non-empty `UploadsPlaylistId` (e.g., `"UU_test_uploads"`); the runner reads it. `LastCheckAt` is irrelevant to the runner — that column is owned by `CollectionRoundService`.
- **GOTCHA**: For the "stops paging when existing video ID seen" test, the fake's `GetVideoDetailsAsync` is called with the *truncated* list (`[v1, v2]`). Assert the fake recorded that call shape — this is what proves the short-circuit happens before enrichment, not after.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj --filter "FullyQualifiedName~ChannelDiscoveryRunnerTests" --nologo -v quiet` exits 0 with 3 passing tests.

### Final Milestone: Commit, push, and open PR (mandatory)

The final milestone of this plan. The autonomous execution loop only ends here.

**Validation checkpoint**: Branch `phase-1-discovery` pushed to `origin`; PR open against `master` with the correct title and body.

#### Task 9: Commit, push, and open PR

After every prior task's VALIDATE has passed:

- **IMPLEMENT**:
  1. From `c:/Repos/Personal/SkipWatch/`, ensure you are on branch `phase-1-discovery` (the autonomous routine creates it from `master` on first execution; if running interactively use `git checkout -b phase-1-discovery master` if not already on it).
  2. Stage all changes:
     ```
     git add SkipWatch.Core/SkipWatch.Core.csproj \
             SkipWatch.Core/Services/Discovery/ \
             SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs \
             SkipWatch.Core/Services/YouTube/YouTubeApiService.cs \
             SkipWatch/Program.cs \
             SkipWatch/Services/Discovery/ \
             SkipWatch/appsettings.json \
             SkipWatch.Tests/Services/Discovery/ \
             docs/phases/phase-1-discovery.md
     ```
  3. Commit with the message:
     ```
     Phase 1: Discovery round

     - Add DiscoverySettings + Discovery config section
     - Extend IYouTubeApiService with ListUploadsPageAsync + GetVideoDetailsAsync
     - Add NCrontab + CronSchedule helper with */N shortcut
     - Add ChannelDiscoveryRunner (per-channel logic) and CollectionRoundService (BackgroundService)
     - Wire DI; remove /debug/yt/channel debug endpoint
     - Tests: CronScheduleTests, ChannelDiscoveryRunnerTests
     ```
  4. Push: `git push -u origin phase-1-discovery`.
  5. Open PR:
     ```
     gh pr create --base master --head phase-1-discovery \
       --title "Phase 1: Discovery round" \
       --body "<see body format below>"
     ```
  6. **PR title format**: `Phase 1: Discovery round`.
  7. **PR body format**: copy the ACCEPTANCE CRITERIA list as a checked-off Markdown checklist, followed by a `## Notes` section enumerating the assumptions documented in the NOTES section of this plan plus anything new that came up during execution.
- **GOTCHA**: `gh` CLI must be installed and authenticated. If `gh auth status` fails, the PR step will not succeed.
- **GOTCHA**: Default branch is `master` (not `main`). `--base master` is mandatory.
- **GOTCHA**: Working directory is `c:/Repos/Personal/SkipWatch/`, not `c:/Repos/Personal/`. `c:/Repos/Personal/` is not a git repo.
- **VALIDATE**:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  gh pr view --json number,title,state,headRefName,baseRefName \
    | python -c "import json,sys; d=json.load(sys.stdin); assert d['state']=='OPEN' and d['title']=='Phase 1: Discovery round' and d['headRefName']=='phase-1-discovery' and d['baseRefName']=='master', d; print('ok')"
  ```

---

## TESTING STRATEGY

### Unit Tests

xUnit v2 with FluentAssertions. New tests in this phase:
- `CronScheduleTests` — 6 cases (3 shortcut, 3 NCrontab) pinning the schedule parser.
- `ChannelDiscoveryRunnerTests` — 3 cases covering cold-start cap, existing-ID short-circuit, and duration-gate bucketing.

These exercise the runner against an in-memory SQLite DbContext (`Database.Migrate()` applies `Initial` + the no-op `AddVideoFts`) with a fake `IYouTubeApiService` so no real API calls happen.

### Integration Tests

`HealthEndpointTests` (Phase 0) continues to pass — the new hosted service starts with the host but does no API call until its first scheduled tick (the round will fail-soft against a misconfigured `YouTube:ApiKey` because the runner catches exceptions and the round logs them, so test boot is unaffected). No new integration test in this phase; the runner tests cover the shape of the work.

### Edge Cases

- **Channel has no new videos**: `ListUploadsPageAsync` returns items but they all already exist → runner inserts 0 rows, `LastCheckAt` still updates. Covered by Task 8 test 2.
- **Channel has fewer uploads than `cap`**: `NextPageToken` is null after the first page → runner stops naturally. Covered by Task 8 test 1 (20 items returned, no second page).
- **Quota refused before `playlistItems.list`**: runner returns `QuotaExceeded = true`, `LastCheckAt` updates, no rows inserted. Smoke-validated in Task 5 logs (manual).
- **Quota refused before `videos.list`**: same as above but after the first call succeeded; the first-page items are dropped (no enrichment data, no insert).
- **`videos.list` returns fewer items than requested**: shouldn't happen for valid IDs, but the runner only inserts items present in *both* the upload page and the details response.
- **Duration unknown**: `DurationParser` returns null → row gets `SkippedTooLong` per PRD ("> MAX_VIDEO_DURATION_MINUTES or unknown").
- **Cron expression invalid**: `CrontabSchedule.Parse` throws at `CronSchedule.Parse` time, which is at hosted-service construction. The host startup fails loudly — the operator must fix the config. This is the desired failure mode; do not silently fall back to a default.

---

## VALIDATION COMMANDS

Execute every command to ensure zero regressions and 100% phase correctness. All commands assume the working directory is `c:/Repos/Personal/SkipWatch`.

### Level 1: Syntax & Style

```bash
cd c:/Repos/Personal/SkipWatch
dotnet format SkipWatch.slnx --verify-no-changes
```

**Expected**: Exit code 0.

### Level 2: Unit Tests

```bash
cd c:/Repos/Personal/SkipWatch
dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj --configuration Debug --nologo
```

**Expected**: All tests pass. Phase 0 tests (2) + new Phase 1 tests (9 total: 6 cron + 3 runner) = 11 passing, 0 failed.

### Level 3: Integration Tests

Same command as Level 2 — `HealthEndpointTests` is the integration test in this phase.

### Level 4: Manual Validation

Single non-interactive smoke test: boot the host with the cron set to fire immediately, confirm the round runs and writes `LastCheckAt` for any pre-existing channel rows. The script seeds a single fake channel before boot so the round has something to pick (without calling the real YouTube API; the round will receive an error from `YouTubeApiService` because `YouTube:ApiKey` is empty, which is fine — the assertion is on `LastCheckAt`, not on inserts).

```bash
cd c:/Repos/Personal/SkipWatch

# Build.
dotnet build SkipWatch.slnx -c Release --nologo -v quiet

# Reset the on-disk SQLite DB to a known state for this smoke test.
DB="$HOME/.skipwatch/skipwatch.db"
rm -f "$DB"
mkdir -p "$HOME/.skipwatch"

# Boot the host with a 1-minute cron and seed a channel before the round picks it.
# The round triggers on startup (the */N shortcut runs immediately).
dotnet run --project SkipWatch/SkipWatch.csproj --no-build --no-launch-profile \
  --urls http://127.0.0.1:7861 \
  -- --Discovery:Cron="*/1 * * * *" > /tmp/sw-run.log 2>&1 &
PID=$!

# Wait for the host to be live (uses the /health endpoint from Phase 0).
for i in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:7861/health > /dev/null 2>&1; then break; fi
  sleep 1
done

# Seed a channel directly into the DB. EF Core has the schema already; the round
# can be told to pick this row by setting LastCheckAt to NULL.
sqlite3 "$DB" "INSERT INTO Channels (YoutubeChannelId, UploadsPlaylistId, Title, Enabled, AddedAt) VALUES ('UC_smoke_test_channel', 'UU_smoke_test_uploads', 'Smoke Test', 1, datetime('now'));"

# Wait up to 90 seconds for the round to update LastCheckAt (or LastCheckError).
ROUND_RAN=0
for i in $(seq 1 90); do
  COUNT=$(sqlite3 "$DB" "SELECT COUNT(*) FROM Channels WHERE LastCheckAt IS NOT NULL;")
  if [ "$COUNT" -ge 1 ]; then ROUND_RAN=1; break; fi
  sleep 1
done

kill $PID 2>/dev/null || true
wait $PID 2>/dev/null || true

test "$ROUND_RAN" = "1"
grep -q "Discovery round starting" /tmp/sw-run.log
grep -q "Discovery round complete" /tmp/sw-run.log
```

**Expected**: Exit 0. The fake channel's `LastCheckAt` is populated, the round logs are present, and the host shuts down cleanly.

### Level 5: Additional Validation (Optional)

None required for Phase 1. Phase 7 (packaging) will add the launcher-script smoke test that exercises the full pipeline.

---

## ACCEPTANCE CRITERIA

- [ ] `DiscoverySettings` exists in `SkipWatch.Core.Services.Discovery` with the seven Phase-1 constants and PRD defaults
- [ ] `appsettings.json` declares the `Discovery` section with all seven defaults
- [ ] `Program.cs` calls `Configure<DiscoverySettings>` against `Configuration.GetSection("Discovery")`
- [ ] `IYouTubeApiService` exposes `ListUploadsPageAsync` and `GetVideoDetailsAsync` with the documented result records
- [ ] `YouTubeApiService` implements both methods, gates each on `IYouTubeQuotaManager.TryConsumeQuotaAsync`, and wraps `GoogleApiException` to set `IsQuotaExceeded`
- [ ] `NCrontab` package is referenced from `SkipWatch.Core.csproj`
- [ ] `CronSchedule.Parse("*/30 * * * *").FixedInterval` equals `TimeSpan.FromMinutes(30)`
- [ ] `CronSchedule.Parse("0 * * * *").FixedInterval` is null and `GetDelayFromUtcNow` returns a positive delay
- [ ] `ChannelDiscoveryRunner` selects `InitialVideoCap` for cold-start channels and `RollingVideoCap` otherwise
- [ ] `ChannelDiscoveryRunner` stops paging the moment any returned `YoutubeVideoId` already exists in `Videos`
- [ ] `ChannelDiscoveryRunner` writes `SkippedShort` when `duration <= MinVideoDurationSeconds` and `SkippedTooLong` when `duration > MaxVideoDurationMinutes * 60` or unknown
- [ ] `CollectionRoundService` uses `IServiceScopeFactory` to resolve `SkipWatchDbContext` per round
- [ ] `CollectionRoundService` selects channels via `Enabled = true AND (LastCheckAt IS NULL OR LastCheckAt < UtcNow - 24h)`, never-checked first, then oldest-checked, capped at `ChannelsPerRound`
- [ ] `CollectionRoundService` updates `LastCheckAt` (and `LastCheckError` on failure) per channel, regardless of runner outcome
- [ ] The `*/N * * * *` cron form runs the round immediately on startup and then every N minutes
- [ ] The `/debug/yt/channel/{handleOrId}` endpoint is removed from `Program.cs`
- [ ] `CronScheduleTests` and `ChannelDiscoveryRunnerTests` pass
- [ ] Phase 0 tests (`SkipWatchDbContextSmokeTests`, `HealthEndpointTests`) still pass
- [ ] `dotnet format SkipWatch.slnx --verify-no-changes` exits 0
- [ ] Branch `phase-1-discovery` pushed to `origin`; PR open against `master` titled `Phase 1: Discovery round`
- [ ] No regressions in existing `Channels.razor`, `Topics.razor`, `MessageSidebar.razor`, harvested CSS, or the `/debug/transcript/{videoId}` endpoint (still owned by Phase 2)

---

## COMPLETION CHECKLIST

- [ ] All tasks completed in order
- [ ] Each task validation passed immediately after the task ran
- [ ] All validation commands executed successfully:
  - [ ] Level 1: `dotnet format SkipWatch.slnx --verify-no-changes`
  - [ ] Level 2: `dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj`
  - [ ] Level 3: covered by Level 2
  - [ ] Level 4: manual smoke script
- [ ] Full test suite passes (11 tests green)
- [ ] No linting errors
- [ ] No formatting errors
- [ ] All acceptance criteria met
- [ ] Branch pushed and PR opened (final milestone task)

---

## NOTES

**Assumptions resolved at plan time (no user clarification asked):**

1. **Phase 1 = channel discovery only.** PRD §6 lists Phase 1 (channels) and Phase 1b (topics) as separate phases. This plan delivers Phase 1 only; topic discovery, the `TopicRoundService`/extension, and the `topic_videos` provenance writes belong to a future `phase-2-…` (or `phase-1b-topics`) plan and are out of scope here.
2. **Runner lives in `SkipWatch.Core`, hosted service in `SkipWatch`.** The runner is pure orchestration over `SkipWatchDbContext` + `IYouTubeApiService` and benefits from sitting in the testable Core library. The hosted service depends on `IServiceScopeFactory` and `BackgroundService`, both of which are available in either project, but keeping ASP.NET-host concerns in the host project matches the existing layout (`SkipWatch.Services.*` for host services, `SkipWatch.Core.Services.*` for reusable logic).
3. **Quota is consumed via `TryConsumeQuotaAsync`, not the reservation API.** The reservation flow exists for multi-step operations whose total cost is known up front. The discovery round's per-channel cost is bounded (1 + 1 = 2 units typical, 1 + ceil(items/50) at cold-start) and the round itself is small enough that optimistic consumption can never burn through the soft ceiling.
4. **`YouTubeApiOperation` enum is not extended.** `GetPlaylistItems` and `GetVideoDetails` are already defined with cost 1 — exactly what the PRD calls for.
5. **`/debug/yt/channel/{handleOrId}` is removed.** PRD §6 Phase 0 commits to removing the two debug endpoints "once their respective workers land." Phase 1 lands the channel-discovery worker, so the channel-resolver debug endpoint goes. `/debug/transcript/{videoId}` is left for Phase 2 to remove.
6. **The `*/N` cron form runs the round immediately on startup.** Operationally, an operator who starts the host expects to see a discovery round in the logs without waiting up to N minutes. The PRD short-circuits to `PeriodicTimer(N)` but does not specify "fire immediately"; this is a decision logged here.
7. **`Channel.LastCheckError` stores the runner's `Error` (or the thrown exception's `Message`).** PRD-implicit but not explicit. Using the message keeps the column human-readable in dashboards; the full stack trace is in the structured logs.
8. **The smoke script's seed channel will produce a `LastCheckError`** (because the test environment has no `YouTube:ApiKey`). That is the assertion target — `LastCheckAt` populated, `LastCheckError` non-null, round logs present. Inserts are not asserted because the test environment has no real API access.
9. **NCrontab pinned to whatever `dotnet add package NCrontab` selects (latest 3.x).** The library has been stable for a long time; locking a specific minor version adds maintenance overhead with no payoff.
10. **No new EF Core migration.** The `Channel` and `Video` entities are unchanged; this phase only writes new rows and updates existing columns. The `Initial` and `AddVideoFts` migrations from Phase 0 are sufficient.

**Trade-offs:**

- The runner short-circuits paging on the *first* existing video ID seen. This means if a YouTube response somehow returns videos out of `publishedAt` order (rare; the uploads playlist is reverse-chronological by API contract), some new videos older than the existing one could be missed. The PRD explicitly accepts this trade-off ("Stop paging the moment we hit a video already in the DB"). Re-checking older items is what cold-start handles.
- The hosted service emits one log line per channel per round (plus start/end markers). At 5 channels × 48 rounds/day = 240 lines/day, this is verbose but useful for the operator-of-one this app is designed for. If logs grow noisy in Phase 7, downgrade per-channel lines from `Information` to `Debug`.
- `dotnet format` runs against the whole solution including the new `SkipWatch.Tests/Services/Discovery/` folder; `.editorconfig` rules apply uniformly.
