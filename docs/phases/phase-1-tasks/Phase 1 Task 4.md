# Phase 1 — Task 4: CREATE `IChannelDiscoveryRunner` + `ChannelDiscoveryRunner`

You are executing one task from SkipWatch Phase 1 (channel discovery round). The full phase plan is at [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md). This file is self-contained for executing Task 4.

## Prerequisites

Tasks 1-3 complete (`DiscoverySettings`, `CronSchedule`, the two new methods on `IYouTubeApiService` + result records).

## Working directory

cwd = `c:/Repos/Personal/SkipWatch`.

## Phase context (why this task exists)

Per-channel discovery logic must be pure orchestration over `SkipWatchDbContext` and `IYouTubeApiService` so it can be unit-tested against an in-memory SQLite DbContext without booting the host. The runner pages uploads, short-circuits on existing IDs, enriches new IDs with video details, applies the duration gate, and inserts rows. The runner does **not** update `Channel.LastCheckAt` — that is the orchestrator's job (Task 5) so the column is updated even when the runner throws.

PRD §6 Phase 1 rule for the duration gate:
- `duration <= MIN_VIDEO_DURATION_SECONDS` → `SkippedShort`
- `duration > MAX_VIDEO_DURATION_MINUTES * 60` or unknown → `SkippedTooLong`
- else → `Discovered`

## Files you MUST read before implementing

- [SkipWatch.Core/Entities/Channel.cs](../../../SkipWatch/SkipWatch.Core/Entities/Channel.cs) — fields the runner reads (`Id`, `UploadsPlaylistId`).
- [SkipWatch.Core/Entities/Video.cs](../../../SkipWatch/SkipWatch.Core/Entities/Video.cs) — fields the runner writes on insert. Note `IngestedAt` defaults to `DateTime.UtcNow`. Enum names: `VideoStatus.Discovered`, `VideoStatus.SkippedShort`, `VideoStatus.SkippedTooLong` (NOT `Skipped_Short`).
- [SkipWatch.Core/Db/SkipWatchDbContext.cs](../../../SkipWatch/SkipWatch.Core/Db/SkipWatchDbContext.cs) — `Videos` and `Channels` DbSets; `idx_videos_q_transcript` is what Phase 2 will read, so newly-inserted rows must satisfy `Status = 'Discovered' AND Parked = 0` to appear in it.
- [SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs](../../../SkipWatch/SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs) — the methods you call: `ListUploadsPageAsync`, `GetVideoDetailsAsync` (added in Task 3), and the result records.
- [SkipWatch/Features/Channels/Services/ChannelService.cs](../../../SkipWatch/SkipWatch/Features/Channels/Services/ChannelService.cs) — pattern for `IYouTubeApiService` consumption (constructor injection, result-record branching, EF Core `SaveChangesAsync` with cancellation token).
- `SkipWatch.Core/Services/Discovery/DiscoverySettings.cs` (created in Task 1) — settings injected via `IOptions<DiscoverySettings>`.

## The task

### IMPLEMENT

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
   - The runner does **not** update `channel.LastCheckAt` itself — `CollectionRoundService` (Task 5) does that.

### PATTERN

`ChannelService.AddAsync` for the EF Core pattern (`FirstOrDefaultAsync`, `Add`, single `SaveChangesAsync` per logical operation, cancellation token propagation).

### IMPORTS

`using Microsoft.EntityFrameworkCore;`, `using Microsoft.Extensions.Logging;`, `using Microsoft.Extensions.Options;`, `using SkipWatch.Core.Db;`, `using SkipWatch.Core.Entities;`, `using SkipWatch.Core.Services.Interfaces;`.

### GOTCHAS

- The "stop paging when an existing ID is hit" rule means *the first existing ID in batch order ends the round for this channel*, not "skip existing IDs and keep going". This matches the PRD's intent: uploads playlist returns newest first, so an existing ID means everything older is already in the DB.
- When the duration gate marks a row `SkippedShort` or `SkippedTooLong`, those rows still get inserted (audit trail). Phase 2's transcript worker filter (`WHERE Status = 'Discovered'`) skips them naturally.
- Channels passed in are already-tracked entities loaded from the DB, so their `Id` is populated; do not set `Video.ChannelId = 0`.
- Tasks 1 and 2 already created files in `SkipWatch.Core/Services/Discovery/`. Confirm the folder exists rather than recreating it.

### VALIDATE

```bash
cd c:/Repos/Personal/SkipWatch
dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
  && grep -q 'class ChannelDiscoveryRunner' SkipWatch.Core/Services/Discovery/ChannelDiscoveryRunner.cs \
  && grep -q 'IChannelDiscoveryRunner' SkipWatch.Core/Services/Discovery/IChannelDiscoveryRunner.cs
```

Exit code must be 0.
