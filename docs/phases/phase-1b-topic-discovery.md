# Phase 1b: Topic discovery

The following plan should be complete, but it is important that you validate documentation and codebase patterns and task sanity before you start implementing.

Pay special attention to naming of existing utils, types, and models. Import from the right files.

## Phase Description

Stand up the second discovery surface described in PRD §6 Phase 1b: per-round topic polling that runs `search.list` against each enabled topic's saved query, enriches new IDs with `videos.list`, applies the same duration gate as channel discovery, and inserts rows as `VideoStatus.Discovered` with provenance recorded in `topic_videos`. Topic-discovered videos enter the same Phase 2 queue as channel-discovered videos — the transcript and summary workers remain oblivious to which surface produced a row.

The phase extends — it does not replace — Phase 1's `CollectionRoundService`. The same `*/30` cadence drives both channel discovery and topic discovery on each tick. Per PRD §6: pick up to `TopicsPerRound = 2` enabled topics not visited in the last 24 hours; cap each topic at `TopicResultsCap = 20` returned IDs; cost is **100 quota units per topic call**. Default math: 2 topics × 100 × 48 rounds/day = ~9,600 units/day, fitting under `CeilingUnits = 9000` once channel discovery's ~480 units are added. The 24-hour gate is enforced strictly so adding many topics spreads coverage across days rather than burning quota in hours.

This phase scopes to **discovery only** — no Library/Project/Pass UI, no source-badge rendering on cards (Phase 4 follow-up per PRD §6), no per-topic filter facet. Apify, the LLM, and the triage UI all stay untouched.

## User Stories

As a SkipWatch user
I want to add a saved YouTube search query as a Topic and have SkipWatch poll it on the same fixed schedule as my followed channels
So that I get new videos that match a research interest even when they come from channels I don't follow.

As a SkipWatch user
I want a video that is found by both a channel I follow and a topic I follow to appear once in my feed with both sources tracked
So that the dashboard never duplicates rows and I can later filter by either surface.

As a SkipWatch operator
I want topic discovery to honor the same 24-hour fairness gate, the same duration filter, and the same quota ceiling as channel discovery
So that adding topics never pushes daily Data API spend over the configured ceiling.

## Problem Statement

Phase 1 wired `CollectionRoundService` to poll followed channels on a cron-driven cadence and write `Discovered` rows. Topics already have an entity (`SkipWatch.Core.Entities.Topic`), a join entity (`TopicVideo`), an `idx_topics_round_pick` index, and an add/remove/list UI under `SkipWatch/Features/Topics/`. Nothing actually polls them. Without the topic round:

- Topics added in the UI sit forever with `LastCheckAt = NULL` and never produce videos.
- The `topic_videos` provenance table stays empty, so future "By topic" facets (Phase 4) have nothing to filter on.
- The `SearchVideos` operation is defined in `YouTubeApiOperationCosts` (cost 100) but has no caller — the quota math in PRD §6 Phase 1b is unenforced.
- The PRD's quota mitigations (`TopicsPerRound = 2`, strict 24-hour gate) live only in prose.

## Solution Statement

Add five discrete pieces, mirroring Phase 1's shape so the resulting code stays symmetric with `ChannelDiscoveryRunner`:

1. **`DiscoverySettings` extension** — two new optional fields: `TopicsPerRound` (default 2) and `TopicResultsCap` (default 20). Bound from the same `Discovery:` configuration section.
2. **`IYouTubeApiService.SearchVideosAsync`** — new method that wraps `search.list` (`q=<query>`, `type=video`, `order=date`, `publishedAfter=<utcNow - lookbackDays>`, `maxResults <= 50`). Costs 1 reservation against `YouTubeApiOperation.SearchVideos` (which `YouTubeApiOperationCosts` already prices at 100). Returns `videoId`, `channelId`, `channelTitle`, `title`, `publishedAt`, `thumbnailUrl` per item — channel info is captured here because the surfacing channel may not be a followed channel.
3. **`TopicDiscoveryRunner`** — pure per-topic logic: call `SearchVideosAsync`, dedupe against existing `Videos.YoutubeVideoId`, ensure each surfacing channel exists in `Channels` (auto-create with `Enabled = false` for unknown channels using snippet data — see NOTES), enrich brand-new IDs with `GetVideoDetailsAsync`, apply the duration gate, insert `Video` rows with `Status = Discovered`, and **always** insert a `TopicVideo` row for every returned ID (whether the video is brand new or already in the DB) so provenance is captured. Lives in `SkipWatch.Core/Services/Discovery/`.
4. **`CollectionRoundService` extension** — after the channel loop, run a topic loop using the same scope: select up to `TopicsPerRound` enabled topics with `LastCheckAt == null || LastCheckAt < cutoff`, hand each to `TopicDiscoveryRunner`, write `LastCheckAt` / `LastCheckError` whether the visit succeeded or not. Same DI scope per tick, same logging shape.
5. **DI registration** — `Program.cs` adds `ITopicDiscoveryRunner → TopicDiscoveryRunner` (scoped, mirroring `IChannelDiscoveryRunner`).

Tests: a `FakeYouTubeApi` fixture (extending the Phase 1 fake) exercises `TopicDiscoveryRunner` against an in-memory SQLite DbContext and asserts row counts, `topic_videos` rows for both new and pre-existing videos, the duration gate, the 20-cap, and the auto-Channel-creation path.

## Phase Metadata

**Phase Type**: New Capability
**Estimated Complexity**: Medium
**Primary Systems Affected**: `SkipWatch.Core/Services/Discovery/` (new runner, settings extension), `SkipWatch.Core/Services/YouTube/YouTubeApiService.cs` (new method), `SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs` (new method + result records), `SkipWatch/Services/Discovery/CollectionRoundService.cs` (topic loop), `SkipWatch/Program.cs` (DI), `SkipWatch/appsettings.json` (two new fields), `SkipWatch.Tests/` (new fixtures).
**Dependencies**: Phase 1 complete (channel discovery merged via PR #5). No new NuGet packages — the Google `search.list` surface is already available via the existing `Google.Apis.YouTube.v3` client. No new external services.

---

## CONTEXT REFERENCES

### Relevant Codebase Files IMPORTANT: YOU MUST READ THESE FILES BEFORE IMPLEMENTING!

- [SkipWatch.Core/Services/YouTube/YouTubeApiService.cs](../../SkipWatch.Core/Services/YouTube/YouTubeApiService.cs) lines 107-167 (`ListUploadsPageAsync`) — the canonical pattern for: quota gate via `TryConsumeQuotaAsync`, reuse of `_youTubeClient`, `Google.GoogleApiException` catch with `IsQuotaExceeded` flag, generic `Exception` fallback. **Mirror this exactly for `SearchVideosAsync`**.
- [SkipWatch.Core/Services/YouTube/YouTubeApiService.cs](../../SkipWatch.Core/Services/YouTube/YouTubeApiService.cs) lines 169-225 (`GetVideoDetailsAsync`) — same shape, used unchanged by the topic runner.
- [SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs](../../SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs) — interface to extend; existing `UploadsPageItem` (line 53), `VideoDetails` (line 65) records sit here. Add `SearchVideosResult` and `SearchVideoItem` records to this same file alongside them.
- [SkipWatch.Core/Services/YouTube/Models/YouTubeApiSettings.cs](../../SkipWatch.Core/Services/YouTube/Models/YouTubeApiSettings.cs) lines 34-44 — `YouTubeApiOperation.SearchVideos` is **already defined** (line 39) and `YouTubeApiOperationCosts` already prices it at 100 (line 113). Do **not** add a new enum value or duplicate the cost entry.
- [SkipWatch.Core/Services/Discovery/ChannelDiscoveryRunner.cs](../../SkipWatch.Core/Services/Discovery/ChannelDiscoveryRunner.cs) — the runner pattern to mirror: ctor with `SkipWatchDbContext` + `IYouTubeApiService` + `IOptions<DiscoverySettings>` + `ILogger<>`; result record returned to caller; quota-exceeded flag bubbles up; `SaveChangesAsync(ct)` at the end. Lines 81-125 are the duration-gate + insert pattern to copy.
- [SkipWatch.Core/Services/Discovery/IChannelDiscoveryRunner.cs](../../SkipWatch.Core/Services/Discovery/IChannelDiscoveryRunner.cs) — interface + `ChannelDiscoveryResult` record shape to mirror for the topic-side equivalents.
- [SkipWatch.Core/Services/Discovery/DiscoverySettings.cs](../../SkipWatch.Core/Services/Discovery/DiscoverySettings.cs) — file to extend with the two new fields.
- [SkipWatch.Core/Entities/Topic.cs](../../SkipWatch.Core/Entities/Topic.cs) — fields the runner reads (`Query`, `LookbackDays`, `Enabled`, `LastCheckAt`) and writes (`LastCheckAt`, `LastCheckError`). `Topic.LookbackDays` defaults to 7.
- [SkipWatch.Core/Entities/TopicVideo.cs](../../SkipWatch.Core/Entities/TopicVideo.cs) — composite-key join row. `DiscoveredAt` defaults to `DateTime.UtcNow`. The runner inserts one of these per returned video ID, **always**, even if the video already existed.
- [SkipWatch.Core/Entities/Video.cs](../../SkipWatch.Core/Entities/Video.cs) — `ChannelId` is `int` (non-nullable). Topic-found videos may surface unknown channels; see NOTES for the auto-create-Channel decision. `IngestedAt` defaults to `DateTime.UtcNow`. Status enum is `VideoStatus.Discovered / SkippedShort / SkippedTooLong`.
- [SkipWatch.Core/Entities/Channel.cs](../../SkipWatch.Core/Entities/Channel.cs) — `Enabled` defaults to `true`. Auto-created channels from topic surfacing must explicitly set `Enabled = false` so they are not picked up by `CollectionRoundService`'s channel loop. Required fields: `YoutubeChannelId`, `UploadsPlaylistId`, `Title`. `UploadsPlaylistId` is non-nullable — see NOTES for how the auto-create path satisfies it without a second API call.
- [SkipWatch.Core/Db/SkipWatchDbContext.cs](../../SkipWatch.Core/Db/SkipWatchDbContext.cs) lines 33-39 — `idx_topics_round_pick`, `TopicVideo` composite PK, `idx_topic_videos_video` are **already in the schema** from Phase 0. No new migration needed.
- [SkipWatch/Services/Discovery/CollectionRoundService.cs](../../SkipWatch/Services/Discovery/CollectionRoundService.cs) — file to extend. The channel loop at lines 70-107 is the template for the topic loop that follows it. `IServiceScopeFactory` is the lifetime gateway — keep using one scope per tick (lines 66-68).
- [SkipWatch/Program.cs](../../SkipWatch/Program.cs) lines 47-49 — channel runner DI registration; topic runner registers directly underneath in the same shape.
- [SkipWatch/appsettings.json](../../SkipWatch/appsettings.json) lines 20-28 — Discovery section; add `TopicsPerRound` and `TopicResultsCap` here. Do **not** raise the `CeilingUnits` default — the 24-hour gate plus the default of 2 topics keep the round under it.
- [SkipWatch.Tests/Services/Discovery/ChannelDiscoveryRunnerTests.cs](../../SkipWatch.Tests/Services/Discovery/ChannelDiscoveryRunnerTests.cs) — in-memory SQLite + `Database.Migrate()` pattern + `FakeYouTubeApi` shape. Mirror exactly for the topic runner test fixture; extend the fake with a `SearchResults` queue and a `SearchCalls` log so the cap and 24h gate can be asserted.
- [docs/prd.md §6 Phase 1b](../prd.md) lines 508-530 — single source of truth for round semantics, the search.list parameter set, the duration gate (inherited from Phase 1), the constant defaults (`TopicsPerRound=2`, `TopicResultsCap=20`), and the explicit non-goal of badge rendering / facets in this phase. **Read before coding.**

### New Files to Create

- `SkipWatch.Core/Services/Discovery/ITopicDiscoveryRunner.cs` — interface + `TopicDiscoveryResult` record (mirrors `IChannelDiscoveryRunner` and `ChannelDiscoveryResult`).
- `SkipWatch.Core/Services/Discovery/TopicDiscoveryRunner.cs` — per-topic logic.
- `SkipWatch.Tests/Services/Discovery/TopicDiscoveryRunnerTests.cs` — unit tests under the existing test project.

### Files to Modify

- `SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs` — add `SearchVideosAsync` method, `SearchVideosResult` record, `SearchVideoItem` record.
- `SkipWatch.Core/Services/YouTube/YouTubeApiService.cs` — implement `SearchVideosAsync` mirroring `ListUploadsPageAsync`.
- `SkipWatch.Core/Services/Discovery/DiscoverySettings.cs` — add `TopicsPerRound = 2`, `TopicResultsCap = 20`.
- `SkipWatch/Services/Discovery/CollectionRoundService.cs` — extend `RunRoundAsync` with the topic loop.
- `SkipWatch/Program.cs` — register `ITopicDiscoveryRunner → TopicDiscoveryRunner` (scoped).
- `SkipWatch/appsettings.json` — add the two new Discovery fields.
- `SkipWatch.Tests/Services/Discovery/ChannelDiscoveryRunnerTests.cs` — its `FakeYouTubeApi` does not implement `SearchVideosAsync`; either add a `NotImplementedException` stub there to keep that test file compiling, or factor the fake into a shared file used by both test classes. The chosen approach is **add the stub** (lower-touch, keeps the file's diff small).

### Relevant Documentation YOU SHOULD READ THESE BEFORE IMPLEMENTING!

- [YouTube Data API v3 — Search.list](https://developers.google.com/youtube/v3/docs/search/list)
  - Specific section: parameter reference (`q`, `type`, `order`, `publishedAfter`, `maxResults`, `safeSearch`)
  - Why: confirms the parameter set used by `SearchVideosAsync`. Note: `publishedAfter` requires RFC 3339 format (`yyyy-MM-ddTHH:mm:ssZ`); the .NET client accepts a string and serializes it for you.
- [YouTube Data API v3 — Quota costs](https://developers.google.com/youtube/v3/determine_quota_cost)
  - Specific section: search.list = 100 units; videos.list = 1 unit
  - Why: confirms the cost numbers already encoded in `YouTubeApiOperationCosts`.
- [Google.Apis.YouTube.v3 SearchResource.ListRequest](https://googleapis.dev/dotnet/Google.Apis.YouTube.v3/latest/api/Google.Apis.YouTube.v3.SearchResource.ListRequest.html)
  - Specific section: `Q`, `Type`, `Order`, `PublishedAfterDateTimeOffset`, `MaxResults`
  - Why: the .NET client field names (PascalCase). `Type` takes a comma-separated string `"video"`. `PublishedAfterDateTimeOffset` takes a `DateTimeOffset?` and is preferred over the deprecated `PublishedAfter` string property.

### Patterns to Follow

**Naming Conventions:**
- Interface: `I<Name>` (e.g., `ITopicDiscoveryRunner`).
- Result record: `<Operation>Result` (e.g., `TopicDiscoveryResult`, `SearchVideosResult`) — sibling of the interface in the same file.
- Item record: `<Operation>Item` for per-element payloads (`SearchVideoItem` mirrors `UploadsPageItem`).
- Sealed records and sealed classes throughout — match the Phase 1 style (`sealed record ChannelDiscoveryResult`, `sealed class ChannelDiscoveryRunner`).

**Error Handling:**
- API call failures wrap into a result record with `Success = false`, `IsQuotaExceeded` set when the underlying `Google.GoogleApiException.HttpStatusCode == HttpStatusCode.Forbidden`, and the API's `Error?.Message` (falling back to `ex.Message`) in `ErrorMessage`. **Do not throw across the runner boundary** — `CollectionRoundService` writes `LastCheckError` from the returned record, not from caught exceptions.
- The runner itself surfaces `TopicDiscoveryResult.Error` for any error condition; the orchestrator (`CollectionRoundService`) wraps the call in `try/catch` only as a last-resort safety net (mirroring lines 84-94 of the existing service).

**Logging Pattern:**
- `_logger.LogInformation("Topic {TopicId} ({Name}) discovery: +{New} discovered, +{TopicLinks} topic_video rows, {Short} short, {Long} too long", ...)` — structured properties, one log line per topic at the runner level + one summary line per round at the service level. Match the wording style of `ChannelDiscoveryRunner` so log scrapers see consistent shapes.

**EF Core Patterns:**
- Use `await _db.SaveChangesAsync(ct)` (not `SaveChanges()`) and pass the `CancellationToken` through.
- Existence checks: `await _db.Videos.AnyAsync(v => v.YoutubeVideoId == id, ct)` — same as Phase 1.
- For the auto-create-Channel path, query `_db.Channels.FirstOrDefaultAsync(c => c.YoutubeChannelId == ytChannelId, ct)`; if null, `_db.Channels.Add(...)` with `Enabled = false`. Save **before** inserting videos so the FK resolves (or use the in-memory navigation property — the simpler approach is one `SaveChangesAsync` after all auto-channel inserts, then a second after all video + topic_video inserts).

---

## IMPLEMENTATION PLAN

**Rendering**: Flat

**Rationale**: 8 tasks total. The work is a single coherent thread (settings → API surface → runner → service wiring → DI → tests → PR), each task has a clear inline VALIDATE, and there are no natural sub-layers worth separating with milestone wrappers. Flat keeps the doc compact and matches the routine's one-task-per-run execution model exactly. The mandatory commit/push/PR milestone is the only milestone, per template.

### Task Authoring Rules (mandatory)

Every task ends with a non-interactive VALIDATE that passes the moment the task completes. Tests added in Task 7 are the durable regression suite; earlier tasks self-validate via `dotnet build` (compilation gate) plus, where the task adds runtime behavior, a `dotnet test --filter` aimed at a tiny test added in the same task.

#### Task 1: Extend `DiscoverySettings` with topic round constants

Add two configuration fields so the topic round has tunable knobs that bind from the existing `Discovery:` configuration section. No code reads them yet — Task 5 introduces the consumer.

- **IMPLEMENT**:
  - Open `SkipWatch.Core/Services/Discovery/DiscoverySettings.cs`.
  - Add `public int TopicsPerRound { get; set; } = 2;` after `ChannelsPerRound`.
  - Add `public int TopicResultsCap { get; set; } = 20;` after `RollingVideoCap`.
  - Open `SkipWatch/appsettings.json` and append `"TopicsPerRound": 2,` and `"TopicResultsCap": 20` inside the `Discovery` object (preserve trailing-comma rules — JSON does not allow them; insert after the existing `MaxRetryAttempts` entry).
- **PATTERN**: existing `ChannelsPerRound` field on `DiscoverySettings.cs` (line 6).
- **IMPORTS**: none new.
- **GOTCHA**: `appsettings.json` is JSON, not JSON5 — no comments, no trailing commas. Verify the file still parses by running `dotnet build` (the configuration binder validates at startup, but build alone exercises the JSON load path via the embedded resource only if the project copies it, so explicitly: `dotnet build` is sufficient because if JSON is malformed, the test in Task 7 that builds the host will fail; for this task, build is the gate).
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo` exits 0.

#### Task 2: Add `SearchVideosAsync` to `IYouTubeApiService` (interface + result records)

Define the contract Task 3 will implement and Task 4 will consume. Pure contract task — no behavior yet.

- **IMPLEMENT**:
  - Open `SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs`.
  - Add the method to the interface, alongside `ListUploadsPageAsync`:
    ```csharp
    /// <summary>
    /// search.list for videos matching <paramref name="query"/> published after
    /// <paramref name="publishedAfterUtc"/>, ordered newest-first. Costs 100 quota units per call.
    /// </summary>
    Task<SearchVideosResult> SearchVideosAsync(
        string query,
        DateTime publishedAfterUtc,
        int maxResults,
        CancellationToken ct = default);
    ```
  - Append the new records at the bottom of the same file:
    ```csharp
    public sealed record SearchVideosResult(
        bool Success,
        IReadOnlyList<SearchVideoItem> Items,
        string? ErrorMessage,
        bool IsQuotaExceeded);

    public sealed record SearchVideoItem(
        string YoutubeVideoId,
        string YoutubeChannelId,
        string ChannelTitle,
        string Title,
        DateTime PublishedAt,
        string? ThumbnailUrl);
    ```
- **PATTERN**: `UploadsPageResult` + `UploadsPageItem` shape (`IYouTubeApiService.cs` lines 46-57). Same field ordering convention: success flag, payload list, error, quota flag.
- **IMPORTS**: none new.
- **GOTCHA**: This task breaks the build of `YouTubeApiService.cs` (does not implement the new method) and the test fakes (do not implement it). Tasks 3 and 6 fix those compilation errors. The VALIDATE for this task therefore tolerates the broken state — explicitly use `dotnet build` against **only the Core project** which has the interface but no implementer in itself; alternatively, defer the build check until after Task 3 by combining Tasks 2+3 mentally and validating Task 3.
- **VALIDATE**: `dotnet build SkipWatch.Core/SkipWatch.Core.csproj --nologo` exits 0 (the Core project's build does not require an implementation of `IYouTubeApiService` because the implementing class is in the same project — so this WILL fail. Therefore the real validation is deferred to Task 3. To honor the no-deferred-validation rule, Task 2's actual VALIDATE is: `grep -q "Task<SearchVideosResult> SearchVideosAsync" SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs && grep -q "public sealed record SearchVideosResult" SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs && grep -q "public sealed record SearchVideoItem" SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs` exits 0). The compilation gate runs at the end of Task 3.

#### Task 3: Implement `SearchVideosAsync` on `YouTubeApiService`

Add the production implementation that calls `search.list` through the existing Google client, gated by `YouTubeApiOperation.SearchVideos` (cost 100, already in `YouTubeApiOperationCosts`).

- **IMPLEMENT**:
  - Open `SkipWatch.Core/Services/YouTube/YouTubeApiService.cs`.
  - Add a `SearchVideosAsync` method below `GetVideoDetailsAsync` (line 221), mirroring `ListUploadsPageAsync`'s structure:
    ```csharp
    public async Task<SearchVideosResult> SearchVideosAsync(
        string query,
        DateTime publishedAfterUtc,
        int maxResults,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchVideosResult(false, Array.Empty<SearchVideoItem>(),
                "Search query is required", false);

        if (!await _quotaManager.TryConsumeQuotaAsync(YouTubeApiOperation.SearchVideos))
            return new SearchVideosResult(false, Array.Empty<SearchVideoItem>(),
                "YouTube API ceiling reached for today. Try again after UTC rollover.", true);

        try
        {
            var request = _youTubeClient.Search.List("snippet");
            request.Q = query;
            request.Type = "video";
            request.Order = SearchResource.ListRequest.OrderEnum.Date;
            request.MaxResults = Math.Min(50, maxResults);
            request.PublishedAfterDateTimeOffset = new DateTimeOffset(
                DateTime.SpecifyKind(publishedAfterUtc, DateTimeKind.Utc));

            var response = await request.ExecuteAsync(ct);

            var items = new List<SearchVideoItem>(response.Items?.Count ?? 0);
            if (response.Items is not null)
            {
                foreach (var item in response.Items)
                {
                    var videoId = item.Id?.VideoId;
                    if (string.IsNullOrEmpty(videoId)) continue;
                    var snippet = item.Snippet;
                    if (snippet is null) continue;

                    var thumb = snippet.Thumbnails?.Default__?.Url
                        ?? snippet.Thumbnails?.Medium?.Url
                        ?? snippet.Thumbnails?.High?.Url;

                    var publishedAt = snippet.PublishedAtDateTimeOffset?.UtcDateTime
                        ?? DateTime.UtcNow;

                    items.Add(new SearchVideoItem(
                        videoId,
                        snippet.ChannelId ?? string.Empty,
                        snippet.ChannelTitle ?? string.Empty,
                        snippet.Title ?? string.Empty,
                        publishedAt,
                        thumb));
                }
            }

            return new SearchVideosResult(true, items, null, false);
        }
        catch (Google.GoogleApiException gex)
        {
            _logger.LogError(gex, "YouTube API error searching for '{Query}'", query);
            var quotaHit = gex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden;
            return new SearchVideosResult(false, Array.Empty<SearchVideoItem>(),
                gex.Error?.Message ?? gex.Message, quotaHit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error searching for '{Query}'", query);
            return new SearchVideosResult(false, Array.Empty<SearchVideoItem>(), ex.Message, false);
        }
    }
    ```
- **PATTERN**: `ListUploadsPageAsync` (`YouTubeApiService.cs` lines 107-167) — same ordering: arg validation → quota gate → try block (build request, execute, project items) → catch `GoogleApiException` with quota detection → catch generic `Exception`.
- **IMPORTS**: existing `using Google.Apis.YouTube.v3;` already covers `SearchResource`. No new usings.
- **GOTCHA**: When `type=video`, search response items have `Id.VideoId` populated (not `Id.Kind`-keyed). Always null-check before reading. Also, `snippet.ChannelId` should never be empty for a `type=video` result, but defensive `?? string.Empty` keeps the runner's auto-create branch from a NRE if it ever is.
- **GOTCHA**: `MaxResults` must be ≤ 50 (YouTube hard cap). Honor the caller's `maxResults` but clamp.
- **GOTCHA**: `PublishedAfterDateTimeOffset` is the non-deprecated property; the older `PublishedAfter` (string) is marked obsolete in current `Google.Apis.YouTube.v3`. Using the offset form avoids a `[Obsolete]` warning, and the project has `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` — an obsolete-warning would fail the build.
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0. (This proves Tasks 1+2+3 compose: settings extension binds, interface change is honored by the implementation, and no obsolete-property warnings.)

#### Task 4: Define `ITopicDiscoveryRunner` interface + result record

Pure contract — interface + sibling record. Sets up the seam Task 6 wires into `CollectionRoundService` and Task 7 mocks in tests.

- **IMPLEMENT**:
  - Create `SkipWatch.Core/Services/Discovery/ITopicDiscoveryRunner.cs`:
    ```csharp
    using SkipWatch.Core.Entities;

    namespace SkipWatch.Core.Services.Discovery;

    public interface ITopicDiscoveryRunner
    {
        Task<TopicDiscoveryResult> RunAsync(Topic topic, CancellationToken ct = default);
    }

    public sealed record TopicDiscoveryResult(
        int NewVideos,
        int NewTopicLinks,
        int SkippedShort,
        int SkippedTooLong,
        bool QuotaExceeded,
        string? Error);
    ```
- **PATTERN**: `IChannelDiscoveryRunner.cs` lines 1-15 — exact mirror, with `NewTopicLinks` added because the topic runner inserts a `TopicVideo` for every result (whether the video was new or pre-existing).
- **IMPORTS**: none new.
- **GOTCHA**: `NewVideos` counts only freshly-inserted `Video` rows in `Discovered` status (mirrors Phase 1's `NewDiscovered`). `NewTopicLinks` counts every `TopicVideo` row inserted in this run — always ≥ `NewVideos` (a video can be a topic link without being a new video).
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo` exits 0.

#### Task 5: Implement `TopicDiscoveryRunner`

Per-topic logic. Calls `SearchVideosAsync`, partitions results into "new IDs" vs "already in DB", auto-creates disabled `Channel` rows for unknown surfacing channels, enriches the new IDs via `GetVideoDetailsAsync`, applies the duration gate, inserts `Video` rows and `TopicVideo` rows.

- **IMPLEMENT**:
  - Create `SkipWatch.Core/Services/Discovery/TopicDiscoveryRunner.cs`. Skeleton:
    ```csharp
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using SkipWatch.Core.Db;
    using SkipWatch.Core.Entities;
    using SkipWatch.Core.Services.Interfaces;

    namespace SkipWatch.Core.Services.Discovery;

    public sealed class TopicDiscoveryRunner : ITopicDiscoveryRunner
    {
        private readonly SkipWatchDbContext _db;
        private readonly IYouTubeApiService _yt;
        private readonly DiscoverySettings _settings;
        private readonly ILogger<TopicDiscoveryRunner> _logger;

        public TopicDiscoveryRunner(
            SkipWatchDbContext db,
            IYouTubeApiService yt,
            IOptions<DiscoverySettings> settings,
            ILogger<TopicDiscoveryRunner> logger)
        { _db = db; _yt = yt; _settings = settings.Value; _logger = logger; }

        public async Task<TopicDiscoveryResult> RunAsync(Topic topic, CancellationToken ct = default)
        {
            var publishedAfter = DateTime.UtcNow.AddDays(-Math.Max(1, topic.LookbackDays));
            var search = await _yt.SearchVideosAsync(topic.Query, publishedAfter, _settings.TopicResultsCap, ct);
            if (!search.Success)
                return new TopicDiscoveryResult(0, 0, 0, 0, search.IsQuotaExceeded, search.ErrorMessage);

            // Cap defensively even if the API returned more.
            var hits = search.Items.Take(_settings.TopicResultsCap).ToList();
            if (hits.Count == 0)
                return new TopicDiscoveryResult(0, 0, 0, 0, false, null);

            // Partition: which IDs are already in `videos`?
            var ytIds = hits.Select(h => h.YoutubeVideoId).ToList();
            var existingVideoIds = await _db.Videos
                .Where(v => ytIds.Contains(v.YoutubeVideoId))
                .ToDictionaryAsync(v => v.YoutubeVideoId, v => v.Id, ct);

            var newHits = hits.Where(h => !existingVideoIds.ContainsKey(h.YoutubeVideoId)).ToList();

            // Ensure a Channel row exists for every surfacing channel of new hits
            // (videos already in DB already have a ChannelId — don't touch them).
            var neededChannelIds = newHits.Select(h => h.YoutubeChannelId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            var existingChannels = await _db.Channels
                .Where(c => neededChannelIds.Contains(c.YoutubeChannelId))
                .ToDictionaryAsync(c => c.YoutubeChannelId, ct);

            foreach (var hit in newHits)
            {
                if (string.IsNullOrEmpty(hit.YoutubeChannelId)) continue;
                if (existingChannels.ContainsKey(hit.YoutubeChannelId)) continue;
                var stub = new Channel
                {
                    YoutubeChannelId = hit.YoutubeChannelId,
                    UploadsPlaylistId = string.Empty, // see NOTES — auto-stub channels carry no uploads playlist
                    Title = string.IsNullOrWhiteSpace(hit.ChannelTitle) ? hit.YoutubeChannelId : hit.ChannelTitle,
                    Enabled = false, // never picked up by the channel round
                };
                _db.Channels.Add(stub);
                existingChannels[hit.YoutubeChannelId] = stub;
            }
            if (newHits.Count > 0) await _db.SaveChangesAsync(ct);

            // Enrich brand-new IDs via videos.list for duration + stats.
            int newVideos = 0, skippedShort = 0, skippedTooLong = 0, newTopicLinks = 0;
            var minSeconds = _settings.MinVideoDurationSeconds;
            var maxSeconds = _settings.MaxVideoDurationMinutes * 60;
            Dictionary<string, VideoDetails> detailsById = new();

            if (newHits.Count > 0)
            {
                var details = await _yt.GetVideoDetailsAsync(newHits.Select(h => h.YoutubeVideoId).ToList(), ct);
                if (!details.Success)
                    return new TopicDiscoveryResult(0, 0, 0, 0, details.IsQuotaExceeded, details.ErrorMessage);
                detailsById = details.Items.ToDictionary(d => d.YoutubeVideoId);
            }

            foreach (var hit in newHits)
            {
                if (!detailsById.TryGetValue(hit.YoutubeVideoId, out var d)) continue;

                VideoStatus status;
                if (d.DurationSeconds is null) { status = VideoStatus.SkippedTooLong; skippedTooLong++; }
                else if (d.DurationSeconds.Value <= minSeconds) { status = VideoStatus.SkippedShort; skippedShort++; }
                else if (d.DurationSeconds.Value > maxSeconds) { status = VideoStatus.SkippedTooLong; skippedTooLong++; }
                else { status = VideoStatus.Discovered; newVideos++; }

                var channelId = existingChannels[hit.YoutubeChannelId].Id;
                var v = new Video
                {
                    YoutubeVideoId = hit.YoutubeVideoId,
                    ChannelId = channelId,
                    Title = hit.Title,
                    PublishedAt = hit.PublishedAt,
                    ThumbnailUrl = hit.ThumbnailUrl,
                    DurationSeconds = d.DurationSeconds,
                    ViewCount = d.ViewCount,
                    LikeCount = d.LikeCount,
                    CommentsCount = d.CommentsCount,
                    Status = status,
                };
                _db.Videos.Add(v);
                _db.TopicVideos.Add(new TopicVideo { Topic = topic, Video = v });
                newTopicLinks++;
            }

            // For pre-existing videos, just insert the topic_videos row (provenance).
            foreach (var hit in hits.Where(h => existingVideoIds.ContainsKey(h.YoutubeVideoId)))
            {
                var videoId = existingVideoIds[hit.YoutubeVideoId];
                var alreadyLinked = await _db.TopicVideos.AnyAsync(
                    tv => tv.TopicId == topic.Id && tv.VideoId == videoId, ct);
                if (alreadyLinked) continue;
                _db.TopicVideos.Add(new TopicVideo { TopicId = topic.Id, VideoId = videoId });
                newTopicLinks++;
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Topic {TopicId} ({Name}) discovery: +{New} videos, +{Links} topic_video rows, {Short} short, {Long} too long",
                topic.Id, topic.Name, newVideos, newTopicLinks, skippedShort, skippedTooLong);

            return new TopicDiscoveryResult(newVideos, newTopicLinks, skippedShort, skippedTooLong, false, null);
        }
    }
    ```
- **PATTERN**: `ChannelDiscoveryRunner.cs` (whole file) — same ctor shape, same `_yt` calls in the same order, same duration gate (lines 81-110), same final `SaveChangesAsync(ct)` + structured log.
- **IMPORTS**: as listed in the skeleton — `Microsoft.EntityFrameworkCore`, the two `Microsoft.Extensions.*`, `SkipWatch.Core.Db`, `SkipWatch.Core.Entities`, `SkipWatch.Core.Services.Interfaces` (for `IYouTubeApiService`, `SearchVideoItem`, `VideoDetails`).
- **GOTCHA**: composite key on `TopicVideo` (`TopicId`, `VideoId`) means two rounds of the same topic returning the same video would collide on insert. The "alreadyLinked" check handles that for pre-existing videos. For brand-new videos in this same run, `Add` builds the row with a Topic+Video graph EF tracks via navigation properties — no collision because the `VideoId` is fresh.
- **GOTCHA**: `Math.Max(1, topic.LookbackDays)` defends against a corrupted/zero `LookbackDays`; the entity defaults to 7 and `TopicService` clamps 1..90 at add-time.
- **GOTCHA**: Do **not** mutate `topic.LastCheckAt` here. The orchestrator (`CollectionRoundService`) writes both `LastCheckAt` and `LastCheckError` after the call returns — same pattern as `ChannelDiscoveryRunner`.
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0.

#### Task 6: Wire `TopicDiscoveryRunner` into `CollectionRoundService` and DI

Run the topic loop after the channel loop on each tick, in the same DI scope. Register the runner.

- **IMPLEMENT**:
  - Open `SkipWatch/Program.cs`. Below the existing `ChannelDiscoveryRunner` registration (line 47-48), add:
    ```csharp
    builder.Services.AddScoped<SkipWatch.Core.Services.Discovery.ITopicDiscoveryRunner,
        SkipWatch.Core.Services.Discovery.TopicDiscoveryRunner>();
    ```
  - Open `SkipWatch/Services/Discovery/CollectionRoundService.cs`. In `RunRoundAsync`, after the channel `foreach` block ends (line 107) and before the closing summary log (line 109), add:
    ```csharp
    var topicRunner = scope.ServiceProvider.GetRequiredService<ITopicDiscoveryRunner>();
    var topics = await db.Topics
        .Where(t => t.Enabled && (t.LastCheckAt == null || t.LastCheckAt < cutoff))
        .OrderBy(t => t.LastCheckAt == null ? 0 : 1)
        .ThenBy(t => t.LastCheckAt)
        .Take(_settings.TopicsPerRound)
        .ToListAsync(ct);

    _logger.LogInformation("Topic round starting. Picked {Count} topic(s).", topics.Count);

    foreach (var topic in topics)
    {
        if (ct.IsCancellationRequested) break;
        TopicDiscoveryResult? tResult = null;
        string? tError = null;
        try
        {
            tResult = await topicRunner.RunAsync(topic, ct);
            tError = tResult.Error;
        }
        catch (Exception ex)
        {
            tError = ex.Message;
            _logger.LogError(ex, "Discovery failed for topic {TopicId} ({Name})",
                topic.Id, topic.Name);
        }

        topic.LastCheckAt = DateTime.UtcNow;
        topic.LastCheckError = tError;
        await db.SaveChangesAsync(ct);

        if (tResult is not null)
        {
            _logger.LogInformation(
                "Topic {TopicId} ({Name}): +{New} videos, +{Links} links, {Short} short, {Long} too long, quotaExceeded={QuotaExceeded}",
                topic.Id, topic.Name,
                tResult.NewVideos, tResult.NewTopicLinks, tResult.SkippedShort, tResult.SkippedTooLong, tResult.QuotaExceeded);
        }
    }
    ```
- **PATTERN**: existing channel loop in the same file (lines 70-107) — same scope reuse, same select-with-fairness ordering, same `LastCheckAt` / `LastCheckError` write pattern.
- **IMPORTS**: file already has `using SkipWatch.Core.Services.Discovery;` — `ITopicDiscoveryRunner` and `TopicDiscoveryResult` resolve from that. No new usings.
- **GOTCHA**: the `cutoff` variable (line 64) is reused — do not redeclare. Same with `db`, `scope`.
- **GOTCHA**: `_settings.TopicsPerRound` reads the field added in Task 1; if it returns 0 (e.g., user explicitly disabled topic discovery in their `appsettings.json`), `Take(0)` short-circuits the query — desired behavior.
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0.

#### Task 7: Tests for `TopicDiscoveryRunner` (and stub the existing fake)

Add a unit test class that mirrors `ChannelDiscoveryRunnerTests` and seal the `IYouTubeApiService` interface change in the existing fake.

- **IMPLEMENT**:
  - Open `SkipWatch.Tests/Services/Discovery/ChannelDiscoveryRunnerTests.cs` and add to the nested `FakeYouTubeApi` class:
    ```csharp
    public Task<SearchVideosResult> SearchVideosAsync(
        string query, DateTime publishedAfterUtc, int maxResults, CancellationToken ct = default) =>
        throw new NotImplementedException();
    ```
  - Create `SkipWatch.Tests/Services/Discovery/TopicDiscoveryRunnerTests.cs` with:
    - In-memory SQLite + `Database.Migrate()` fixture (copy from the channel test file).
    - A `FakeYouTubeApi` (sibling nested class, similar shape) that exposes:
      - `Queue<SearchVideosResult> SearchResults`
      - `List<(string Q, DateTime After, int Max)> SearchCalls`
      - `Queue<VideoDetailsResult> VideoDetailsResponses`
      - `List<IReadOnlyList<string>> VideoDetailsCalls`
      - stubs (`throw new NotImplementedException`) for `GetChannelInfoAsync`, `ListUploadsPageAsync`.
    - Test: `New_search_results_insert_videos_and_topic_links` — seed an empty DB and a topic, enqueue 3 search hits with 3 distinct channel IDs, enqueue matching `VideoDetails` (durations 600s each), assert: 3 new `Video` rows in `Discovered`, 3 new `TopicVideo` rows, 3 auto-created `Channel` rows with `Enabled = false`.
    - Test: `Pre_existing_video_only_inserts_topic_link` — seed a `Video` (and its `Channel`) in the DB, enqueue a search hit pointing at that same `YoutubeVideoId`, assert: no new `Video` row, exactly one new `TopicVideo` row, `videos.list` was **never** called (`VideoDetailsCalls` empty).
    - Test: `Duration_gate_routes_to_correct_status_buckets` — seed empty, enqueue 3 hits (60s short, 600s ok, 4000s long) with distinct channel IDs, assert returned counts and statuses match the channel-runner test's pattern.
    - Test: `Cap_clamps_at_TopicResultsCap` — set `TopicResultsCap = 5`, enqueue 8 search hits (all distinct channel IDs and video IDs) with valid durations, assert exactly 5 new videos and 5 new topic links.
    - Test: `Quota_exceeded_on_search_returns_flag_and_skips_videos_list` — enqueue a `SearchVideosResult(false, [], "ceiling", IsQuotaExceeded: true)`, assert `result.QuotaExceeded == true`, no `videos.list` call, no rows inserted.
- **PATTERN**: `ChannelDiscoveryRunnerTests.cs` lines 14-180 — copy the `NewDb`, runner-construction, and FactBased style verbatim. The project uses xUnit + FluentAssertions (`.Should()`).
- **IMPORTS**: same set as the channel runner test (Microsoft.Data.Sqlite, Microsoft.EntityFrameworkCore, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, SkipWatch.Core.*).
- **GOTCHA**: when seeding the duplicate-video test, the seeded `Video` needs a valid `ChannelId` — seed a real `Channel` row first and use its `.Id`. The runner's "pre-existing" branch doesn't try to look up the channel because the video already has one.
- **GOTCHA**: `Database.Migrate()` applies the FTS5 raw-SQL migration too; tests using `:memory:` SQLite still need to keep the connection open for the test lifetime (the fixture pattern from the channel test does this — copy it).
- **VALIDATE**: `dotnet test SkipWatch.slnx --nologo --filter "FullyQualifiedName~TopicDiscoveryRunnerTests"` exits 0 with all five tests passing; **and** `dotnet test SkipWatch.slnx --nologo` (full suite) exits 0 to confirm no regressions in Phase 1's tests.

#### Task 8: Commit, push, and open PR

After every prior task's VALIDATE has passed, finish the phase by shipping it.

- **IMPLEMENT**:
  - Confirm the branch is `phase-1b-topic-discovery` (create from `master` if not: `git checkout -b phase-1b-topic-discovery master`).
  - `git add -A` and commit any uncommitted changes from earlier tasks with a message summarizing the phase.
  - `git push -u origin phase-1b-topic-discovery`.
  - Open the PR:
    - **Title**: `Phase 1b: Topic discovery`
    - **Body**: copy the ACCEPTANCE CRITERIA section as a checklist with each box checked off, followed by a `## Notes` section listing the assumptions in the NOTES section of this plan plus anything new that came up during implementation.
- **GOTCHA**: `gh` must be installed and authenticated (it was used in Phase 1 — assume present). If absent, the operator must run `gh auth login` before this task.
- **GOTCHA**: the working tree on `master` currently has 10 unrelated deletions (`docs/progress.md` and `docs/phases/phase-1-tasks/Phase 1 Task N.md`). Do NOT include those in this phase's commit — either restore them with `git checkout -- docs/progress.md docs/phases/phase-1-tasks/` before branching, or commit them separately on `master` first. The phase-1b branch should contain **only** Phase 1b changes.
- **VALIDATE**: `gh pr view --json number,title,state,headRefName` returns the new PR with `"state": "OPEN"` and `"headRefName": "phase-1b-topic-discovery"`.

---

## TESTING STRATEGY

The project uses **xUnit + FluentAssertions** (per `SkipWatch.Tests/Usings.cs` and existing test files). In-memory SQLite (`Microsoft.Data.Sqlite` `Data Source=:memory:` with `Database.Migrate()`) is the substitute for a real DB — it exercises the actual EF model and indexes, including the FTS5 raw-SQL migration.

### Unit Tests

Five tests on `TopicDiscoveryRunner` covering:
- new-video insert path (with auto-Channel creation)
- pre-existing-video path (topic link only, no `videos.list` call)
- duration gate (short / ok / too-long routing)
- `TopicResultsCap` enforcement
- quota-exceeded short-circuit

Plus the existing `ChannelDiscoveryRunnerTests` continue to pass with the new `SearchVideosAsync` stub on its `FakeYouTubeApi`.

### Integration Tests

`HealthEndpointTests` already verifies the host wires up; the topic-runner DI registration is implicitly covered when the test host boots without DI errors. No new integration test is required for this phase per PRD §6 Phase 1b's scope (no UI surface, no API endpoint).

### Edge Cases

Covered by the unit tests above:
- Topic with `LookbackDays = 0` is clamped to `1` in the runner — consider adding a sixth test if implementation time allows.
- A topic returning no results (`SearchVideosResult.Items` empty) — handled by the early-return path; not separately tested but exercised in the quota-exceeded case.
- Same video found by two topics in successive rounds — the existence check on `_db.TopicVideos.AnyAsync` prevents duplicate `(TopicId, VideoId)` rows.

---

## VALIDATION COMMANDS

The project's primary tooling is `dotnet` (no `pyproject.toml`, no `package.json`). Solution file is `SkipWatch.slnx`.

Execute every command to ensure zero regressions and 100% phase correctness.

### Level 1: Build & Style

```bash
dotnet build SkipWatch.slnx --nologo /warnaserror
```

The repo's `Directory.Build.props` already sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, so the explicit `/warnaserror` flag is belt-and-braces. No separate `dotnet format` step is enforced by CI today (`.github/workflows/ci.yml` runs `dotnet build` + `dotnet test`); the style gate is the build itself.

**Expected**: exit code 0, zero warnings.

### Level 2: Unit Tests

```bash
dotnet test SkipWatch.slnx --nologo
```

**Expected**: every test in `SkipWatch.Tests` passes, including the new `TopicDiscoveryRunnerTests` and the unchanged Phase 1 tests.

### Level 3: Integration Tests

Same command — the integration-flavored `HealthEndpointTests` lives in the same project and runs as part of the full suite.

### Level 4: Manual Validation

```bash
# Boot the host without booting a real round (no API key configured, no /health hit).
# Confirms DI wiring resolves ITopicDiscoveryRunner without errors.
dotnet build SkipWatch.slnx --nologo /warnaserror \
  && dotnet run --project SkipWatch/SkipWatch.csproj --no-build --launch-profile http &
SERVER_PID=$!
# Poll until the HTTP listener answers /health, then tear down.
for i in $(seq 1 30); do
  if curl -fsS http://localhost:5000/health > /dev/null 2>&1 || curl -fsS http://localhost:5028/health > /dev/null 2>&1; then
    kill $SERVER_PID 2>/dev/null || true
    wait $SERVER_PID 2>/dev/null || true
    exit 0
  fi
  sleep 1
done
kill $SERVER_PID 2>/dev/null || true
wait $SERVER_PID 2>/dev/null || true
exit 1
```

The launch profile `http` uses port 5028 by default in `Properties/launchSettings.json` — adjust if the URL differs locally. The poll loop accepts either of the two common dev ports. **Skip this step if running headless without a `YouTube:ApiKey` configured**: the host will throw at `YouTubeApiService` construction. The Level 1 + 2 commands fully cover the phase; Level 4 is informational on a developer workstation.

### Level 5: Additional Validation (Optional)

None for this phase.

---

## ACCEPTANCE CRITERIA

- [ ] `DiscoverySettings` has `TopicsPerRound` (default 2) and `TopicResultsCap` (default 20); `appsettings.json` carries the same defaults.
- [ ] `IYouTubeApiService.SearchVideosAsync` exists, gated by `YouTubeApiOperation.SearchVideos` (cost 100), and `YouTubeApiService` implements it without compiler warnings.
- [ ] `ITopicDiscoveryRunner` + `TopicDiscoveryRunner` exist in `SkipWatch.Core/Services/Discovery/` and follow the channel runner's structure.
- [ ] Topic-discovered videos land with `VideoStatus.Discovered` (or `SkippedShort` / `SkippedTooLong`); the same video found by both a channel and a topic is **not** duplicated in `videos`.
- [ ] Every search hit produces a `TopicVideo` row (provenance), even when the video already existed.
- [ ] Unknown surfacing channels are auto-created as `Channel` rows with `Enabled = false`; the channel round never picks them up.
- [ ] `CollectionRoundService` runs the topic loop after the channel loop on each tick using the same DI scope and `*/30` cadence; topics not visited in 24 hours are picked, capped at `TopicsPerRound`.
- [ ] `topic.LastCheckAt` is updated whether the visit succeeded or failed; `topic.LastCheckError` carries any error.
- [ ] All five new unit tests pass; full `dotnet test` suite passes with no regressions.
- [ ] `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0.

---

## COMPLETION CHECKLIST

- [ ] All tasks completed in order
- [ ] Each task validation passed immediately
- [ ] All validation commands executed successfully:
  - [ ] Level 1: `dotnet build SkipWatch.slnx --nologo /warnaserror`
  - [ ] Level 2: `dotnet test SkipWatch.slnx --nologo`
  - [ ] Level 3: same (full suite)
  - [ ] Level 4: manual host-boot smoke (skipped if running headless without an API key)
- [ ] Full test suite passes (unit + integration)
- [ ] No build warnings (treated as errors)
- [ ] All acceptance criteria met
- [ ] Code reviewed for quality and maintainability
- [ ] Branch `phase-1b-topic-discovery` pushed and PR opened with title `Phase 1b: Topic discovery`

---

## NOTES

**Decision: extend `CollectionRoundService` instead of adding a sibling `TopicRoundService`.** PRD §6 Phase 1b explicitly allows either ("`CollectionRoundService` is extended (or a sibling `TopicRoundService` is added)"). Extending wins because: (a) one tick per cadence is cheaper than two, (b) sharing the scope means one DbContext per tick instead of two, (c) the channel loop and topic loop are conceptually one "discovery round" and grouping them keeps the round summary log coherent. Trade-off: a slow topic loop blocks the next channel loop on the same tick — acceptable because both loops are fast (under a second of wall time at the configured caps; the long-pole latency is the API round-trip, not local work).

**Decision: auto-create disabled `Channel` rows for unknown surfacing channels rather than making `Video.ChannelId` nullable.** The PRD doesn't dictate the FK shape. Auto-creating with `Enabled = false`:
- Preserves the existing schema's NOT NULL FK with no migration.
- Keeps `Video → Channel` navigation simple in queries (no null guards).
- Surfaces the channel name on the dashboard card without a join through `topic_videos`.
- The `Enabled = false` flag means `CollectionRoundService`'s channel-loop SQL `WHERE c.Enabled` skips them automatically — they never become a polling target.
- `UploadsPlaylistId` is set to `string.Empty` for stub channels (the type is non-nullable). The channel loop's `WHERE c.Enabled` filter ensures these rows never reach `ListUploadsPageAsync` where the empty value would matter. If a user later "promotes" a stub channel by adding it through the channel-add UI, `ChannelService.AddAsync` already calls `GetChannelInfoAsync` and sets the real uploads playlist via the upsert path (`SkipWatch/Features/Channels/Services/ChannelService.cs:47-58` updates the existing row rather than creating a duplicate).

**Decision: capture channel info from `search.list`'s snippet rather than running a second `channels.list` per surfacing channel.** Saves 1 quota unit per unique surfacing channel per round at the cost of a slightly less-rich stub row (no thumbnail, no handle). Both fields are nullable on `Channel` so this is fine.

**Decision: defer source-badge UI rendering to Phase 4.** PRD §6 Phase 1b explicitly defers "Filters and search (Phase 4 follow-up)" — this phase intentionally doesn't touch any `.razor` component. Cards continue to render channel-only metadata; topic provenance lives in the DB until Phase 4 surfaces it.

**Quota math sanity-check (defaults applied).** Channels: 5 per round × 2 calls × 48 rounds/day = 480 units/day. Topics: 2 per round × (1 search.list + ≤1 videos.list) × 48 = ~9,600 units/day worst case. Total worst case ~10,080 — slightly over the 10,000 hard daily quota and the 9,000 ceiling. The 24-hour gate per topic + the empty-result short-circuit keep the actual burn well below worst case in practice; if a user adds many topics, they should lower `TopicsPerRound` to 1 (~4,800/day) or raise `CeilingUnits` if they have an elevated quota allocation. This trade-off is documented here so the operator (and a future reader) can tune without re-deriving the math.
