# Phase 1 — Task 8: CREATE `ChannelDiscoveryRunnerTests`

You are executing one task from SkipWatch Phase 1 (channel discovery round). The full phase plan is at [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md). This file is self-contained for executing Task 8.

## Prerequisites

Tasks 3 and 4 complete (`IYouTubeApiService` extensions + result records, `IChannelDiscoveryRunner` + `ChannelDiscoveryRunner`).

## Working directory

cwd = `c:/Repos/Personal/SkipWatch`.

## Phase context (why this task exists)

Three behaviors earn this phase its keep and must be locked down:
1. **Cold-start cap** — the runner picks `InitialVideoCap` for first visit, `RollingVideoCap` thereafter.
2. **Short-circuit on existing IDs** — paging stops the moment a returned `YoutubeVideoId` already exists in `Videos`.
3. **Duration gate** — `<= MinVideoDurationSeconds` → `SkippedShort`; `> MaxVideoDurationMinutes * 60` or unknown → `SkippedTooLong`; else → `Discovered`.

Tests exercise the runner against an in-memory SQLite DbContext with a fake `IYouTubeApiService` so no real API calls happen.

## Files you MUST read before implementing

- [SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs](../../../SkipWatch/SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs) — in-memory SQLite fixture pattern: `SqliteConnection("Data Source=:memory:")` kept open for the test lifetime, `Database.Migrate()` to apply migrations.
- [SkipWatch.Tests/Usings.cs](../../../SkipWatch/SkipWatch.Tests/Usings.cs) — global usings for `Xunit` / `FluentAssertions`.
- [SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs](../../../SkipWatch/SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs) — interface to fake; result record shapes.
- [SkipWatch.Core/Entities/Channel.cs](../../../SkipWatch/SkipWatch.Core/Entities/Channel.cs) and [SkipWatch.Core/Entities/Video.cs](../../../SkipWatch/SkipWatch.Core/Entities/Video.cs) — required fields when seeding.
- `SkipWatch.Core/Services/Discovery/ChannelDiscoveryRunner.cs` (Task 4) — type under test.

## The task

### IMPLEMENT

Create `SkipWatch.Tests/Services/Discovery/ChannelDiscoveryRunnerTests.cs`:

- Set up an in-memory SQLite DbContext exactly as `SkipWatchDbContextSmokeTests` does (open a `SqliteConnection("Data Source=:memory:")` for the test lifetime, call `Database.Migrate()`).
- Define a `FakeYouTubeApi : IYouTubeApiService` test double with public lists for the queued `UploadsPageResult` returns and the `VideoDetailsResult` returns. `GetChannelInfoAsync` throws `NotImplementedException` (not exercised here). The fake should record every call so tests can assert call shape.
- Use `IOptions.Create(new DiscoverySettings { InitialVideoCap = 20, RollingVideoCap = 10, MinVideoDurationSeconds = 180, MaxVideoDurationMinutes = 60 })` for settings, and `NullLogger<ChannelDiscoveryRunner>.Instance`.

Three tests:

1. **`Cold_start_inserts_up_to_initial_cap`** — queue an `UploadsPageResult` with 20 items (ids `v1`..`v20`, durations all 600s after enrichment) and assert 20 `Video` rows with `Status = Discovered` are written.
2. **`Stops_paging_when_existing_video_id_seen`** — pre-insert one `Video` (`YoutubeVideoId = "v3"`) tied to the channel; queue an `UploadsPageResult` with `[v1, v2, v3, v4, v5]`. Assert only `v1` and `v2` are inserted (paging stops at `v3`); the enrichment call goes out for `[v1, v2]` only.
3. **`Duration_gate_places_rows_in_correct_status_buckets`** — queue 3 items (`vshort` 60s, `vok` 600s, `vlong` 4000s) and assert exactly one row each of `SkippedShort`, `Discovered`, `SkippedTooLong`.

### PATTERN

`SkipWatchDbContextSmokeTests` for the in-memory SQLite fixture; production `IYouTubeApiService` consumers (`ChannelService`) for the result-record shape.

### IMPORTS

`Microsoft.Data.Sqlite`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `SkipWatch.Core.Db`, `SkipWatch.Core.Entities`, `SkipWatch.Core.Services.Discovery`, `SkipWatch.Core.Services.Interfaces`, `SkipWatch.Core.Services.YouTube.Models`.

### GOTCHAS

- Each test owns its own `SqliteConnection` and DbContext; do not share via class fixture. The connection must remain open for the full test (closing flushes the in-memory DB).
- The `Channel` you seed must have a non-empty `UploadsPlaylistId` (e.g., `"UU_test_uploads"`); the runner reads it. `LastCheckAt` is irrelevant to the runner — that column is owned by `CollectionRoundService`.
- For the "stops paging when existing video ID seen" test, the fake's `GetVideoDetailsAsync` is called with the *truncated* list (`[v1, v2]`). Assert the fake recorded that call shape — this is what proves the short-circuit happens before enrichment, not after.

### VALIDATE

```bash
cd c:/Repos/Personal/SkipWatch
dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj --filter "FullyQualifiedName~ChannelDiscoveryRunnerTests" --nologo -v quiet
```

Must exit 0 with 3 passing tests.
