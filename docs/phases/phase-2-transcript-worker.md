# Phase 2: Transcript worker (Q1: Apify)

The following plan should be complete, but it is important that you validate documentation and codebase patterns and task sanity before you start implementing.

Pay special attention to naming of existing utils, types, and models. Import from the right files.

## Phase Description

Stand up the second pipeline stage described in PRD §6 Phase 2: a long-running `BackgroundService` (`TranscriptWorker`) that drains the queue of `VideoStatus.Discovered` rows by calling the existing `ITranscriptSource.FetchAsync` (Apify) for one video at a time, persists the transcript + the richer Apify cheap-field metadata, and transitions the row to `VideoStatus.Transcribed`. The worker also implements the per-row retry/park semantics defined for every queue stage in the PRD: `RetryCount++` on failure with exponential backoff via `NextAttemptAt`, and `Parked = 1` once `RetryCount >= MaxRetryAttempts`. Videos for which Apify returned no usable subtitle land in the terminal `VideoStatus.NoTranscript` status — the LLM is never invoked for them, but the dashboard (Phase 4) will still let the user triage them.

The wiring needed for this phase is small because Phase 0 already migrated every column the worker writes (`TranscriptText`, `TranscriptLang`, `HasTranscript`, `TranscribedAt`, `Description`, `RetryCount`, `LastError`, `NextAttemptAt`, `Parked`, `ParkedAt`) and added the partial index `idx_videos_q_transcript` filtered on `"Status" = 'Discovered' AND "Parked" = 0`. Phase 0 also wired `ITranscriptSource → ApifyTranscriptSource` through `IHttpClientFactory` and proved it end-to-end via the temporary `/debug/transcript/{videoId}` endpoint in `Program.cs`. Phase 2 replaces that debug endpoint with a real worker and removes it.

This phase scopes to **transcript ingestion only** — no summarization (Phase 3), no triage UI (Phase 4), no circuit breaker around Apify outages (PRD §8 risk; deferred). The summary worker continues to find nothing in `VideoStatus.Transcribed` until Phase 3 lands.

## User Stories

As a SkipWatch user
I want every newly discovered video to automatically gain a timestamped transcript without me clicking anything
So that the next pipeline stage can summarize it and the card shows up on my dashboard ready for triage.

As a SkipWatch user
I want videos that have no captions at all to surface on my dashboard with a "no transcript" badge rather than getting stuck mid-pipeline
So that I can still decide to file or pass them based on title, channel, and description alone.

As a SkipWatch operator
I want transient Apify failures to be retried with exponential backoff and persistently failing rows to be parked off the active queue
So that one bad video never blocks the rest and I can manually retry parked rows from the UI later.

## Problem Statement

After Phase 1 / 1b discovery runs, `Videos` rows accumulate in `VideoStatus.Discovered` with no automated path forward. The Apify integration (`ApifyTranscriptSource`) exists and is reachable, but the only caller is the temporary `/debug/transcript/{videoId}` endpoint — there is no background process pulling from the queue. Every column the worker would write is already migrated, every retry-state column already exists, and the partial index `idx_videos_q_transcript` is already in place — yet nothing transitions `Discovered → Transcribed`. The downstream summary worker (Phase 3) and triage UI (Phase 4) cannot start because their input set is empty.

## Solution Statement

Add a `TranscriptWorker : BackgroundService` that runs a single continuous async loop. On each tick it picks **one** eligible row from the Q1 queue using the existing partial index, calls `ITranscriptSource.FetchAsync`, and writes the result. The row's terminal state is one of:

- **`Transcribed`** — transcript came back, all fields written, `RetryCount` reset to 0; this is what Phase 3 will pick up.
- **`NoTranscript`** — Apify succeeded but returned no usable subtitle; the cheap-field columns are still overwritten with the fresher Apify values, `Description` is captured, but `TranscriptText` stays null and the row is terminal for this worker.
- **(stays `Discovered`)** — Apify call threw or returned `Success = false`. `RetryCount++`, `NextAttemptAt = now + min(60s × 2^retry_count, 1h)`, `LastError = exception/error message`. The row stays in the queue and the next eligible tick picks it up.
- **(stays `Discovered`, `Parked = 1`)** — once `RetryCount >= MaxRetryAttempts` the row is removed from the active queue (the partial index filter `"Parked" = 0` excludes it). A future Phase 4 retry button resets `RetryCount = 0` and `Parked = 0`.

Idle behavior: when no row is eligible, the worker sleeps `IdlePollSeconds` (default 10 s) before re-querying. Backoff is enforced inside the SQL filter so a row whose `NextAttemptAt` is in the future is skipped without a wasted Apify call.

Concurrency is fixed at **1** for MVP (PRD §6 Phase 2 default). The worker's loop is sequential; raising it would require a worker-pool refactor that the PRD defers. The setting exists (`TranscriptWorkerSettings.Concurrency`) so Phase 7's settings page can surface it as read-only metadata for now and toggle it in v2.

The worker delegates all per-row logic to a separately-injectable `ITranscriptIngestRunner` so the test suite can exercise the success/no-transcript/transient-failure/park transitions without standing up a hosted service. This mirrors the Phase 1 split between `CollectionRoundService` (the host) and `ChannelDiscoveryRunner` (the per-unit logic).

## Phase Metadata

**Phase Type**: New Capability
**Estimated Complexity**: Medium
**Primary Systems Affected**: `SkipWatch.Core/Services/Transcripts/` (new runner + settings), `SkipWatch/Services/Workers/` (new worker BackgroundService), `SkipWatch/Program.cs` (DI + remove debug endpoint), `SkipWatch/appsettings.json` (new section), `SkipWatch.Tests/Services/Transcripts/` (new test fixture).
**Dependencies**: Phase 1 complete (channel discovery merged via PR #5) — produces the `Discovered` rows the worker drains. Phase 0 migrations (`Initial`, `AddVideoFts`) already provide every column and the partial index this worker reads/writes. `ApifyTranscriptSource` is already registered. **No new NuGet packages.** **No new EF migration.**

---

## CONTEXT REFERENCES

### Relevant Codebase Files IMPORTANT: YOU MUST READ THESE FILES BEFORE IMPLEMENTING!

- [SkipWatch.Core/Entities/Video.cs](../../SkipWatch.Core/Entities/Video.cs) — every column the worker reads or writes already exists: `Status` (line 24), `RetryCount` / `LastError` / `NextAttemptAt` / `Parked` / `ParkedAt` (lines 27-31), `Description` (line 21), `TranscriptText` / `TranscriptLang` / `HasTranscript` / `TranscribedAt` (lines 36-39), and the cheap-field columns to overwrite: `DurationSeconds` / `ViewCount` / `LikeCount` / `CommentsCount` / `ThumbnailUrl` (lines 13-17). `VideoStatus.NoTranscript` is already in the enum (line 56). **Do not add fields or migrate.**
- [SkipWatch.Core/Db/SkipWatchDbContext.cs](../../SkipWatch.Core/Db/SkipWatchDbContext.cs) lines 53-56 — `idx_videos_q_transcript` partial index, filtered on `"Status" = 'Discovered' AND "Parked" = 0`, ordered by `(NextAttemptAt, IngestedAt)`. The worker's queue query MUST hit this index — match its column order and filter exactly. Do not introduce a new index.
- [SkipWatch.Core/Services/Interfaces/ITranscriptSource.cs](../../SkipWatch.Core/Services/Interfaces/ITranscriptSource.cs) — the one method the worker calls: `Task<Transcript> FetchAsync(string videoId, CancellationToken ct = default)`. Already DI-registered as a typed `HttpClient` consumer via `AddHttpClient<ITranscriptSource, ApifyTranscriptSource>` (`Program.cs:40`). Do not register a second time.
- [SkipWatch.Core/Services/Transcripts/Transcript.cs](../../SkipWatch.Core/Services/Transcripts/Transcript.cs) — record returned by `FetchAsync`. Field semantics:
  - `Success = false` → call failed (network, Apify error, timeout). Treat as transient: retry with backoff. `ErrorMessage` carries the reason.
  - `Success = true && HasTranscript = true` → transcript present in `TranscriptText` (already in `[mm:ss] line` format via `SrtConverter`). Transition to `Transcribed`.
  - `Success = true && HasTranscript = false` → Apify returned a usable record but no captions. Transition to `NoTranscript` (terminal).
  - In all `Success = true` cases, the cheap fields (`Description`, `DurationSeconds`, `ViewCount`, `LikeCount`, `CommentsCount`, `ThumbnailUrl`) may have non-null values; overwrite the `Video` row with whichever ones are non-null (preserve existing values when Apify returns null).
- [SkipWatch.Core/Services/Transcripts/ApifyTranscriptSource.cs](../../SkipWatch.Core/Services/Transcripts/ApifyTranscriptSource.cs) — implementation reference. Note: `OperationCanceledException` from a caller-driven cancel is **rethrown** (the `when (ex is not OperationCanceledException)` filter on line 112). The worker must therefore allow `OperationCanceledException` to propagate during shutdown without classifying it as a retryable failure.
- [SkipWatch.Core/Services/Transcripts/ApifySettings.cs](../../SkipWatch.Core/Services/Transcripts/ApifySettings.cs) — already bound to the `Apify:` config section in `Program.cs:32`. The worker does not read it directly — `ApifyTranscriptSource` already does.
- [SkipWatch.Core/Services/Discovery/ChannelDiscoveryRunner.cs](../../SkipWatch.Core/Services/Discovery/ChannelDiscoveryRunner.cs) — the per-unit-logic split to mirror. Same ctor shape: `SkipWatchDbContext` + the external service interface + `IOptions<...Settings>` + `ILogger<>`. Same final `await _db.SaveChangesAsync(ct)` and structured log line. **Mirror the structure** for `TranscriptIngestRunner`.
- [SkipWatch.Core/Services/Discovery/IChannelDiscoveryRunner.cs](../../SkipWatch.Core/Services/Discovery/IChannelDiscoveryRunner.cs) — the interface + result-record sibling pattern (`ChannelDiscoveryResult`). Mirror for `ITranscriptIngestRunner` + `TranscriptIngestResult`.
- [SkipWatch.Core/Services/Discovery/DiscoverySettings.cs](../../SkipWatch.Core/Services/Discovery/DiscoverySettings.cs) line 11 — `MaxRetryAttempts` (default 3) is already defined here. **Reuse it.** `TranscriptWorkerSettings` should NOT redeclare it; the runner takes `IOptions<DiscoverySettings>` (for `MaxRetryAttempts`) plus `IOptions<TranscriptWorkerSettings>` (for the new transcript-only knobs).
- [SkipWatch/Services/Discovery/CollectionRoundService.cs](../../SkipWatch/Services/Discovery/CollectionRoundService.cs) — the `BackgroundService` template to mirror for `TranscriptWorker`: `IServiceScopeFactory` ctor injection, `ExecuteAsync` loop with `stoppingToken` plumbing, `using var scope = _scopeFactory.CreateScope()` per tick, the `try { ... } catch (OperationCanceledException) { /* shutdown */ } catch (Exception ex) { _logger.LogError(...) }` outer wrapper around each tick. `CollectionRoundService` uses `PeriodicTimer` for fixed intervals — `TranscriptWorker` uses `Task.Delay(IdlePollSeconds)` only when no row was eligible (busy-poll otherwise), because a productive tick should immediately try the next row.
- [SkipWatch/Program.cs](../../SkipWatch/Program.cs) lines 32 (`AddSingleton...QuotaManager`), 40 (`AddHttpClient<ITranscriptSource, ApifyTranscriptSource>`), 47-49 (channel runner + hosted service registration), 86-109 (the `/debug/transcript/{videoId}` endpoint to **remove** in Task 6). The DI shape is the template for the new worker registrations.
- [SkipWatch/appsettings.json](../../SkipWatch/appsettings.json) — add a new top-level `"TranscriptWorker"` section. Do not modify the existing `Apify` or `Discovery` sections.
- [SkipWatch.Tests/Services/Discovery/ChannelDiscoveryRunnerTests.cs](../../SkipWatch.Tests/Services/Discovery/ChannelDiscoveryRunnerTests.cs) — fixture template: `NewDb()` opens an in-memory SQLite connection and runs `Database.Migrate()`, the connection is kept open for the test lifetime via `using var _ = conn;`, the runner is constructed with a `Fake<Service>` + `Options.Create(settings)` + `NullLogger<...>.Instance`. **Mirror exactly.** xUnit + FluentAssertions are already wired via `SkipWatch.Tests/Usings.cs`.
- [SkipWatch.Core/Entities/ActivityEntry.cs](../../SkipWatch.Core/Entities/ActivityEntry.cs) — the activity log entity. The PRD's `Kind = 'transcript'` with `Outcome ∈ {'ok','fail','parked'}` is the contract. Phase 2 writes one `ActivityEntry` per processed row at the runner level.
- [docs/prd.md §6 Phase 2](../prd.md) lines 532-550 — single source of truth for the queue query, the per-video step list, the retry/park semantics, the backoff formula `min(60s × 2^retry_count, 1h)`, the `TRANSCRIPT_WORKER_CONCURRENCY` default, and the `no_transcript` decision. **Read before coding.**

### New Files to Create

- `SkipWatch.Core/Services/Transcripts/TranscriptWorkerSettings.cs` — bound from the new `TranscriptWorker:` config section.
- `SkipWatch.Core/Services/Transcripts/ITranscriptIngestRunner.cs` — interface + `TranscriptIngestResult` sibling record.
- `SkipWatch.Core/Services/Transcripts/TranscriptIngestRunner.cs` — per-video logic.
- `SkipWatch/Services/Workers/TranscriptWorker.cs` — `BackgroundService` host.
- `SkipWatch.Tests/Services/Transcripts/TranscriptIngestRunnerTests.cs` — unit tests.

### Files to Modify

- `SkipWatch/Program.cs` — register `ITranscriptIngestRunner` (scoped) and `TranscriptWorker` (hosted service); remove the `/debug/transcript/{videoId}` endpoint.
- `SkipWatch/appsettings.json` — add the `TranscriptWorker` section with documented defaults.

### Relevant Documentation YOU SHOULD READ THESE BEFORE IMPLEMENTING!

- [.NET BackgroundService](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers)
  - Specific section: "Implement IHostedService with BackgroundService"
  - Why: confirms the cancellation-token plumbing pattern. The worker MUST honor `stoppingToken` on every await; the host triggers it on app shutdown and waits up to `HostOptions.ShutdownTimeout` (default 30s) for the loop to exit.
- [.NET BackgroundService — graceful shutdown](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers#scope-of-the-background-service)
  - Specific section: "Avoid catching OperationCanceledException"
  - Why: explicit guidance that matches PRD §6 Phase 2's "while (!stoppingToken.IsCancellationRequested)" pattern. The worker treats `OperationCanceledException` as a shutdown signal, never a retryable failure.
- [EF Core in dependency injection — DbContext lifetime](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-with-dependency-injection)
  - Specific section: "DbContext in dependency injection for ASP.NET Core"
  - Why: `SkipWatchDbContext` is registered scoped (`AddDbContext` defaults to scoped). A `BackgroundService` is a singleton, so it MUST resolve the context inside a per-tick scope — exactly what `CollectionRoundService` does today.
- [Apify run-sync-get-dataset-items](https://docs.apify.com/api/v2#tag/Actor-runsRun-actor-synchronously-and-get-dataset-items)
  - Specific section: response codes
  - Why: 408/500/502/503/504 are transient; 401/402/403 are usually credential or quota and will be transient from the worker's POV (retry until the operator fixes credentials), at which point park is the right outcome. The worker doesn't need to discriminate — `Transcript.Success = false` is a single bucket; the runner's exponential backoff + park threshold is the universal recovery mechanism.

### Patterns to Follow

**Naming Conventions:**
- Interface: `ITranscriptIngestRunner`. Implementation: `TranscriptIngestRunner` (sealed class). Result record: `TranscriptIngestResult` (sealed record), declared as a sibling in `ITranscriptIngestRunner.cs` — same file layout as `IChannelDiscoveryRunner.cs`.
- Worker: `TranscriptWorker : BackgroundService` (sealed). Lives in `SkipWatch/Services/Workers/` (new folder; `Workers/` is the right home for hosted services that drain queues — `Discovery/` is reserved for the cron-driven round). Namespace: `SkipWatch.Services.Workers`.
- Settings: `TranscriptWorkerSettings` (no `I` prefix; not an interface). Lives next to `ApifySettings` in `SkipWatch.Core/Services/Transcripts/`.

**Error Handling:**
- The runner's `RunAsync(Video, CancellationToken)` returns a `TranscriptIngestResult` for every outcome (success, no-transcript, retried, parked). It does NOT throw across its boundary — even an unexpected `Exception` from `_yt.FetchAsync` is caught, classified as a transient failure, and reflected in the result. This mirrors `ChannelDiscoveryRunner`'s policy of bubbling status through the result record. The single exception is `OperationCanceledException` driven by `ct` — that propagates so the worker can shut down cleanly.
- The worker wraps each tick in `try { ... } catch (OperationCanceledException) { return; } catch (Exception ex) { _logger.LogError(ex, ...); await Task.Delay(IdlePollSeconds, stoppingToken); }` — same shape as `CollectionRoundService.SafeRunRoundAsync`.

**Logging Pattern:**
- Runner: one structured log line per processed row at `Information`:
  `"Transcript ingest video {VideoId} ({YoutubeVideoId}): {Outcome} (retry={Retry}, parked={Parked}) in {ElapsedMs}ms"` with `Outcome ∈ {"transcribed","no_transcript","retry","parked"}`.
- Worker: one `Information` line on start (`"TranscriptWorker starting. concurrency={Concurrency} idlePoll={IdlePollSeconds}s"`) and one on stop. Per-tick logging is the runner's job.

**EF Core Patterns:**
- Queue query: `_db.Videos.Where(v => v.Status == VideoStatus.Discovered && !v.Parked && (v.NextAttemptAt == null || v.NextAttemptAt <= now)).OrderBy(v => v.NextAttemptAt).ThenBy(v => v.IngestedAt).FirstOrDefaultAsync(ct)`. The combination of (`Status='Discovered'`, `Parked=false`) hits the partial-index filter; the (`NextAttemptAt`, `IngestedAt`) order matches the index's column order. Verify via `dotnet ef migrations script` if you want to confirm the SQL — but the schema is unchanged, so the existing index is automatically used.
- All `await` calls take the `CancellationToken`. The runner threads `ct` to `_db.SaveChangesAsync(ct)` and to `_transcripts.FetchAsync(video.YoutubeVideoId, ct)`.
- `DateTime.UtcNow` for timestamps (the entire codebase is UTC).

---

## IMPLEMENTATION PLAN

**Rendering**: Flat

**Rationale**: 7 tasks total, all on a single coherent thread (settings → contract → runner → worker → DI → tests → commit/PR). The work has no natural sub-layers worth surfacing under milestone wrappers — each task has its own per-task VALIDATE that gates it, and a flat list matches the autonomous routine's one-task-per-run model exactly. The mandatory commit/push/PR task is the only milestone-style wrapper, per template.

### Task Authoring Rules (mandatory)

Every task ends with a non-interactive VALIDATE that runs the moment the task completes. Tasks 1-5 self-validate via `dotnet build SkipWatch.slnx --nologo /warnaserror` (compilation gate; the repo's `Directory.Build.props` enforces warnings-as-errors, so the build doubles as the style gate). Task 6 adds the durable regression suite. Task 7 ships the phase.

#### Task 1: Add `TranscriptWorkerSettings`

Define the new configuration type the worker and runner will consume. Two fields: `Concurrency` (PRD calls this `TRANSCRIPT_WORKER_CONCURRENCY`, default 1) and `IdlePollSeconds` (default 10 — how long the worker sleeps when the queue is empty).

- **IMPLEMENT**:
  - Create `SkipWatch.Core/Services/Transcripts/TranscriptWorkerSettings.cs`:
    ```csharp
    namespace SkipWatch.Core.Services.Transcripts;

    /// <summary>
    /// Phase 2 transcript worker tuning. Bound from the <c>TranscriptWorker:</c>
    /// configuration section. Per PRD §6 Phase 2: concurrency is fixed at 1 in MVP — local
    /// LLM and serial Apify spend make parallelism a v2 concern.
    /// </summary>
    public sealed class TranscriptWorkerSettings
    {
        /// <summary>Worker concurrency. MVP: 1.</summary>
        public int Concurrency { get; set; } = 1;

        /// <summary>Idle sleep interval when no row is eligible.</summary>
        public int IdlePollSeconds { get; set; } = 10;
    }
    ```
  - Open `SkipWatch/appsettings.json` and append a new top-level section after `Discovery`:
    ```json
    "TranscriptWorker": {
      "Concurrency": 1,
      "IdlePollSeconds": 10
    }
    ```
    Insert a comma after the closing `}` of `Discovery` to keep the JSON valid; place the new section before the file's closing `}`.
- **PATTERN**: `ApifySettings.cs` (sibling file) — same one-class-per-file shape, sealed, public setters, defaults inline. The doc comment above the class follows the same prose style as `ApifySettings`.
- **IMPORTS**: none new.
- **GOTCHA**: `appsettings.json` is JSON, not JSON5 — no comments, no trailing commas. After the edit, `dotnet build` will load and bind the file at startup if a test boots the host; the build alone exercises only the JSON schema indirectly. The hard validation is Task 5's DI wiring.
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0.

#### Task 2: Define `ITranscriptIngestRunner` interface + result record

Define the seam the worker depends on and the test fixture mocks. Pure contract task — no behavior yet.

- **IMPLEMENT**:
  - Create `SkipWatch.Core/Services/Transcripts/ITranscriptIngestRunner.cs`:
    ```csharp
    using SkipWatch.Core.Entities;

    namespace SkipWatch.Core.Services.Transcripts;

    public interface ITranscriptIngestRunner
    {
        /// <summary>
        /// Process a single Discovered/Parked=false video row. Always returns a result; never throws
        /// across this boundary except for caller-driven <see cref="OperationCanceledException"/>.
        /// </summary>
        Task<TranscriptIngestResult> RunAsync(Video video, CancellationToken ct = default);
    }

    public enum TranscriptIngestOutcome
    {
        Transcribed,
        NoTranscript,
        Retry,
        Parked,
    }

    public sealed record TranscriptIngestResult(
        TranscriptIngestOutcome Outcome,
        int RetryCount,
        string? Error,
        int ElapsedMs);
    ```
- **PATTERN**: `IChannelDiscoveryRunner.cs` — interface + sibling result record in the same file. The new `TranscriptIngestOutcome` enum is needed here (not in `Video.cs`) because it's a *transition* signal returned by the runner, not a stored state on the entity.
- **IMPORTS**: `SkipWatch.Core.Entities` (for `Video`).
- **GOTCHA**: The `Outcome` values do not map 1:1 to `VideoStatus`. `Retry` and `Parked` both leave `Video.Status = Discovered` (with different `Parked`/`NextAttemptAt` state); only `Transcribed` and `NoTranscript` change `Video.Status`.
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0.

#### Task 3: Implement `TranscriptIngestRunner`

Per-video logic. All four outcome paths land here.

- **IMPLEMENT**:
  - Create `SkipWatch.Core/Services/Transcripts/TranscriptIngestRunner.cs`:
    ```csharp
    using System.Diagnostics;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using SkipWatch.Core.Db;
    using SkipWatch.Core.Entities;
    using SkipWatch.Core.Services.Discovery;
    using SkipWatch.Core.Services.Interfaces;

    namespace SkipWatch.Core.Services.Transcripts;

    public sealed class TranscriptIngestRunner : ITranscriptIngestRunner
    {
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);
        private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(60);

        private readonly SkipWatchDbContext _db;
        private readonly ITranscriptSource _transcripts;
        private readonly DiscoverySettings _discovery;
        private readonly ILogger<TranscriptIngestRunner> _logger;

        public TranscriptIngestRunner(
            SkipWatchDbContext db,
            ITranscriptSource transcripts,
            IOptions<DiscoverySettings> discovery,
            ILogger<TranscriptIngestRunner> logger)
        {
            _db = db;
            _transcripts = transcripts;
            _discovery = discovery.Value;
            _logger = logger;
        }

        public async Task<TranscriptIngestResult> RunAsync(Video video, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            Transcript transcript;
            try
            {
                transcript = await _transcripts.FetchAsync(video.YoutubeVideoId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transcript fetch threw for video {VideoId}", video.YoutubeVideoId);
                transcript = new Transcript(false, null, null, false, null, null, null, null, null, null, ex.Message);
            }

            sw.Stop();
            var elapsedMs = (int)sw.ElapsedMilliseconds;

            // Failure path: bump retry/park, leave Status=Discovered.
            if (!transcript.Success)
            {
                video.RetryCount++;
                video.LastError = transcript.ErrorMessage ?? "unknown error";

                if (video.RetryCount >= _discovery.MaxRetryAttempts)
                {
                    video.Parked = true;
                    video.ParkedAt = DateTime.UtcNow;
                    video.NextAttemptAt = null;
                    _db.Activity.Add(new ActivityEntry
                    {
                        Kind = "transcript", RefId = video.Id, Outcome = "parked",
                        Detail = video.LastError, DurationMs = elapsedMs,
                    });
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "Transcript ingest video {VideoId} ({Yt}): parked (retry={Retry}, parked=True) in {Elapsed}ms",
                        video.Id, video.YoutubeVideoId, video.RetryCount, elapsedMs);
                    return new TranscriptIngestResult(TranscriptIngestOutcome.Parked, video.RetryCount, video.LastError, elapsedMs);
                }

                var backoff = TimeSpan.FromTicks(Math.Min(
                    MaxBackoff.Ticks,
                    BaseBackoff.Ticks * (long)Math.Pow(2, video.RetryCount - 1)));
                video.NextAttemptAt = DateTime.UtcNow.Add(backoff);
                _db.Activity.Add(new ActivityEntry
                {
                    Kind = "transcript", RefId = video.Id, Outcome = "fail",
                    Detail = video.LastError, DurationMs = elapsedMs,
                });
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Transcript ingest video {VideoId} ({Yt}): retry (retry={Retry}, parked=False) in {Elapsed}ms",
                    video.Id, video.YoutubeVideoId, video.RetryCount, elapsedMs);
                return new TranscriptIngestResult(TranscriptIngestOutcome.Retry, video.RetryCount, video.LastError, elapsedMs);
            }

            // Success: overwrite cheap fields when Apify supplied non-null values.
            if (transcript.Description is not null) video.Description = transcript.Description;
            if (transcript.DurationSeconds is not null) video.DurationSeconds = transcript.DurationSeconds;
            if (transcript.ViewCount is not null) video.ViewCount = transcript.ViewCount;
            if (transcript.LikeCount is not null) video.LikeCount = transcript.LikeCount;
            if (transcript.CommentsCount is not null) video.CommentsCount = transcript.CommentsCount;
            if (!string.IsNullOrEmpty(transcript.ThumbnailUrl)) video.ThumbnailUrl = transcript.ThumbnailUrl;

            if (transcript.HasTranscript)
            {
                video.TranscriptText = transcript.TranscriptText;
                video.TranscriptLang = transcript.TranscriptLang;
                video.HasTranscript = true;
                video.TranscribedAt = DateTime.UtcNow;
                video.Status = VideoStatus.Transcribed;
            }
            else
            {
                video.HasTranscript = false;
                video.TranscriptText = null;
                video.TranscriptLang = null;
                video.Status = VideoStatus.NoTranscript;
            }

            // Status changed -> reset retry state per PRD §6 Phase 2 step 4.
            video.RetryCount = 0;
            video.LastError = null;
            video.NextAttemptAt = null;

            var outcome = video.Status == VideoStatus.Transcribed
                ? TranscriptIngestOutcome.Transcribed
                : TranscriptIngestOutcome.NoTranscript;

            _db.Activity.Add(new ActivityEntry
            {
                Kind = "transcript",
                RefId = video.Id,
                Outcome = outcome == TranscriptIngestOutcome.Transcribed ? "ok" : "no_transcript",
                Detail = null,
                DurationMs = elapsedMs,
            });
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Transcript ingest video {VideoId} ({Yt}): {Outcome} (retry=0, parked=False) in {Elapsed}ms",
                video.Id, video.YoutubeVideoId,
                outcome == TranscriptIngestOutcome.Transcribed ? "transcribed" : "no_transcript",
                elapsedMs);

            return new TranscriptIngestResult(outcome, 0, null, elapsedMs);
        }
    }
    ```
- **PATTERN**: `ChannelDiscoveryRunner.cs` — same ctor (db + service interface + IOptions + ILogger), same single `await _db.SaveChangesAsync(ct)` per outcome path, same structured log. The `OperationCanceledException` filter mirrors the inverse-condition pattern in `ApifyTranscriptSource.cs:112` (rethrow on caller-driven cancel, swallow otherwise — here we rethrow on cancel and convert any other exception to a `Transcript` failure record so the retry path runs uniformly).
- **IMPORTS**: as listed in the skeleton. `System.Diagnostics` for `Stopwatch`. `Microsoft.EntityFrameworkCore` is needed for the `SaveChangesAsync` extension. `SkipWatch.Core.Services.Discovery` brings in `DiscoverySettings`. `SkipWatch.Core.Services.Interfaces` brings in `ITranscriptSource`.
- **GOTCHA**: the backoff uses `RetryCount - 1` because `RetryCount` is incremented *before* computing the backoff (so on the first failure `RetryCount=1`, backoff = 60s × 2^0 = 60s; second failure 120s; third 240s; etc.). PRD §6 Phase 2 specifies `min(60s × 2^retry_count, 1h)` — interpret `retry_count` in the formula as "how many retries have already happened, including this one". The cap at 1h kicks in at `RetryCount = 7` (60s × 64 = 64 minutes → clamped). Tests must lock this in.
- **GOTCHA**: do NOT update `IngestedAt` on transition. The queue's tiebreak is `IngestedAt ASC`; mutating it would re-prioritize the row.
- **GOTCHA**: rule per PRD §6 Phase 2 step 4 — `RetryCount` resets to 0 on **status transition** (Transcribed or NoTranscript). It also resets implicitly when an operator unparks a row (Phase 4 retry button); not handled here.
- **GOTCHA**: write `ActivityEntry.Outcome = "no_transcript"` for the terminal-no-captions case, not `"ok"`. The ActivityEntry doc lists the canonical outcomes — adding `"no_transcript"` is a low-risk extension consistent with the other phase-specific outcomes (`skipped_short`, `skipped_too_long`).
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0.

#### Task 4: Implement `TranscriptWorker` BackgroundService

Continuous-loop host that picks one row at a time from the Q1 queue and hands it to the runner.

- **IMPLEMENT**:
  - Create the folder `SkipWatch/Services/Workers/` (alongside `SkipWatch/Services/Discovery/`).
  - Create `SkipWatch/Services/Workers/TranscriptWorker.cs`:
    ```csharp
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
    ```
- **PATTERN**: `CollectionRoundService.cs` — same `IServiceScopeFactory` + `IOptions<...>` + `ILogger<>` ctor, same `using var scope = _scopeFactory.CreateScope()` per tick, same outer try/catch shape on the loop body.
- **IMPORTS**: as listed.
- **GOTCHA**: the queue query uses `(v.NextAttemptAt == null || v.NextAttemptAt <= now)` so backoff is enforced inside SQL — no eligible row is touched until its `NextAttemptAt` has passed. The `ORDER BY NextAttemptAt, IngestedAt` matches the `idx_videos_q_transcript` column order; the partial-index filter `Status='Discovered' AND Parked=0` matches the `Where`. EF Core's SQLite provider will use the index — confirmed by the same query-shape pattern Phase 1 uses against `idx_channels_round_pick`.
- **GOTCHA**: per-tick scope is mandatory because `SkipWatchDbContext` is registered scoped (default `AddDbContext`) — sharing one across ticks would leak the change-tracker.
- **GOTCHA**: `Concurrency` is *not* yet honored — MVP runs serially. Surfacing the setting for Phase 7 even though it's unused is intentional. Do not introduce a worker pool; the PRD defers it.
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0.

#### Task 5: Wire DI, bind settings, remove debug endpoint

Register the runner, the worker, and the settings binding; drop the no-longer-needed `/debug/transcript/{videoId}` endpoint.

- **IMPLEMENT**:
  - Open `SkipWatch/Program.cs`. Below the existing `builder.Services.Configure<DiscoverySettings>(...)` line (around line 33), add:
    ```csharp
    builder.Services.Configure<TranscriptWorkerSettings>(builder.Configuration.GetSection("TranscriptWorker"));
    ```
  - Below the existing `builder.Services.AddScoped<...IChannelDiscoveryRunner, ...ChannelDiscoveryRunner>();` registration (around line 47-48), add:
    ```csharp
    builder.Services.AddScoped<SkipWatch.Core.Services.Transcripts.ITranscriptIngestRunner,
        SkipWatch.Core.Services.Transcripts.TranscriptIngestRunner>();
    ```
  - Below the existing `builder.Services.AddHostedService<...CollectionRoundService>();` registration (around line 49), add:
    ```csharp
    builder.Services.AddHostedService<SkipWatch.Services.Workers.TranscriptWorker>();
    ```
  - Add `using SkipWatch.Core.Services.Transcripts;` to the top of `Program.cs` if it is not already present (`ApifySettings` is already imported via the same namespace; if its `using` is already there, no new using is required for `TranscriptWorkerSettings`).
  - Remove the entire `app.MapGet("/debug/transcript/{videoId}", ...)` block (currently lines ~86-109 of `Program.cs`). PRD §6 Phase 2 marks it for removal once the worker is wired up. Also remove the `// H6 validation endpoint —` prose comment immediately above it.
- **PATTERN**: existing `Configure<DiscoverySettings>`, `AddScoped<IChannelDiscoveryRunner, ...>`, `AddHostedService<CollectionRoundService>` lines in `Program.cs` are the templates. Mirror line-for-line.
- **IMPORTS**: `SkipWatch.Core.Services.Transcripts` is already in scope via the `ApifySettings` configure call — no new using needed (verify after the edit).
- **GOTCHA**: removing the debug endpoint also removes the only consumer of `ITranscriptSource` from `Program.cs`; the worker now consumes it via `TranscriptIngestRunner`. The `AddHttpClient<ITranscriptSource, ApifyTranscriptSource>(...)` line stays exactly as-is — `IHttpClientFactory` resolves typed clients per-scope, which is what the runner needs.
- **GOTCHA**: do not touch the `app.MapGet("/health", ...)` endpoint — the launcher script depends on it (PRD §6 Phase 7).
- **VALIDATE**: `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0 **and** `grep -c "/debug/transcript" SkipWatch/Program.cs` outputs `0` (the debug route is gone).

#### Task 6: Tests for `TranscriptIngestRunner`

The runner is the only piece with non-trivial behavior; the worker is a thin loop. Cover all four outcome paths plus the backoff math and the cheap-field overwrite rule.

- **IMPLEMENT**:
  - Create `SkipWatch.Tests/Services/Transcripts/TranscriptIngestRunnerTests.cs`. Skeleton:
    ```csharp
    using Microsoft.Data.Sqlite;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.Extensions.Options;
    using SkipWatch.Core.Db;
    using SkipWatch.Core.Entities;
    using SkipWatch.Core.Services.Discovery;
    using SkipWatch.Core.Services.Interfaces;
    using SkipWatch.Core.Services.Transcripts;

    namespace SkipWatch.Tests.Services.Transcripts;

    public sealed class TranscriptIngestRunnerTests
    {
        private static (SqliteConnection conn, SkipWatchDbContext db) NewDb()
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            var options = new DbContextOptionsBuilder<SkipWatchDbContext>()
                .UseSqlite(conn).Options;
            var db = new SkipWatchDbContext(options);
            db.Database.Migrate();
            return (conn, db);
        }

        private static Channel SeedChannel(SkipWatchDbContext db)
        {
            var ch = new Channel
            {
                YoutubeChannelId = "UC_test", UploadsPlaylistId = "UU_test", Title = "Test"
            };
            db.Channels.Add(ch);
            db.SaveChanges();
            return ch;
        }

        private static Video SeedVideo(SkipWatchDbContext db, int channelId, string ytId = "yt_v1")
        {
            var v = new Video
            {
                YoutubeVideoId = ytId, ChannelId = channelId, Title = "T",
                PublishedAt = DateTime.UtcNow, Status = VideoStatus.Discovered,
                DurationSeconds = 600, ViewCount = 100, LikeCount = 10, CommentsCount = 1,
            };
            db.Videos.Add(v);
            db.SaveChanges();
            return v;
        }

        private static TranscriptIngestRunner NewRunner(SkipWatchDbContext db, FakeTranscriptSource src, int maxRetry = 3) =>
            new(db, src,
                Options.Create(new DiscoverySettings { MaxRetryAttempts = maxRetry }),
                NullLogger<TranscriptIngestRunner>.Instance);

        [Fact]
        public async Task Success_with_transcript_writes_text_and_status_transcribed()
        {
            var (conn, db) = NewDb(); using var _ = conn; using var __ = db;
            var ch = SeedChannel(db); var v = SeedVideo(db, ch.Id);

            var src = new FakeTranscriptSource();
            src.Responses.Enqueue(new Transcript(true,
                "[00:00] hello\n[00:05] world", "en", true,
                "fresh description", 999, 5000, 200, 30, "https://thumb", null));

            var result = await NewRunner(db, src).RunAsync(v);

            result.Outcome.Should().Be(TranscriptIngestOutcome.Transcribed);
            var saved = db.Videos.Single();
            saved.Status.Should().Be(VideoStatus.Transcribed);
            saved.HasTranscript.Should().BeTrue();
            saved.TranscriptText.Should().Be("[00:00] hello\n[00:05] world");
            saved.TranscriptLang.Should().Be("en");
            saved.TranscribedAt.Should().NotBeNull();
            saved.RetryCount.Should().Be(0);
            saved.LastError.Should().BeNull();
            // cheap-field overwrite from Apify
            saved.Description.Should().Be("fresh description");
            saved.DurationSeconds.Should().Be(999);
            saved.ViewCount.Should().Be(5000);
            db.Activity.Single().Outcome.Should().Be("ok");
        }

        [Fact]
        public async Task Success_without_transcript_lands_in_NoTranscript_status()
        {
            var (conn, db) = NewDb(); using var _ = conn; using var __ = db;
            var ch = SeedChannel(db); var v = SeedVideo(db, ch.Id);

            var src = new FakeTranscriptSource();
            // Apify came back fine but no captions at all.
            src.Responses.Enqueue(new Transcript(true, null, null, false,
                "desc only", 600, null, null, null, null, null));

            var result = await NewRunner(db, src).RunAsync(v);

            result.Outcome.Should().Be(TranscriptIngestOutcome.NoTranscript);
            var saved = db.Videos.Single();
            saved.Status.Should().Be(VideoStatus.NoTranscript);
            saved.HasTranscript.Should().BeFalse();
            saved.TranscriptText.Should().BeNull();
            saved.Description.Should().Be("desc only");
            saved.RetryCount.Should().Be(0);
            db.Activity.Single().Outcome.Should().Be("no_transcript");
        }

        [Fact]
        public async Task Cheap_fields_are_not_overwritten_when_Apify_returns_null()
        {
            var (conn, db) = NewDb(); using var _ = conn; using var __ = db;
            var ch = SeedChannel(db); var v = SeedVideo(db, ch.Id); // ViewCount=100

            var src = new FakeTranscriptSource();
            src.Responses.Enqueue(new Transcript(true, "[00:00] x", "en", true,
                Description: null, DurationSeconds: null, ViewCount: null,
                LikeCount: null, CommentsCount: null, ThumbnailUrl: null, ErrorMessage: null));

            await NewRunner(db, src).RunAsync(v);

            var saved = db.Videos.Single();
            saved.ViewCount.Should().Be(100); // preserved
            saved.DurationSeconds.Should().Be(600);
        }

        [Fact]
        public async Task Failure_increments_retry_and_sets_backoff()
        {
            var (conn, db) = NewDb(); using var _ = conn; using var __ = db;
            var ch = SeedChannel(db); var v = SeedVideo(db, ch.Id);

            var src = new FakeTranscriptSource();
            src.Responses.Enqueue(new Transcript(false, null, null, false,
                null, null, null, null, null, null, "Apify 502 Bad Gateway"));

            var before = DateTime.UtcNow;
            var result = await NewRunner(db, src, maxRetry: 3).RunAsync(v);
            var after = DateTime.UtcNow;

            result.Outcome.Should().Be(TranscriptIngestOutcome.Retry);
            var saved = db.Videos.Single();
            saved.Status.Should().Be(VideoStatus.Discovered);
            saved.RetryCount.Should().Be(1);
            saved.LastError.Should().Be("Apify 502 Bad Gateway");
            saved.Parked.Should().BeFalse();
            // First failure -> 60s backoff (60 * 2^0).
            saved.NextAttemptAt.Should().NotBeNull();
            (saved.NextAttemptAt!.Value - before).TotalSeconds.Should().BeApproximately(60, 5);
            db.Activity.Single().Outcome.Should().Be("fail");
        }

        [Fact]
        public async Task Failure_at_max_retries_parks_the_row()
        {
            var (conn, db) = NewDb(); using var _ = conn; using var __ = db;
            var ch = SeedChannel(db); var v = SeedVideo(db, ch.Id);
            v.RetryCount = 2; // one more failure pushes it to 3 = MaxRetryAttempts
            db.SaveChanges();

            var src = new FakeTranscriptSource();
            src.Responses.Enqueue(new Transcript(false, null, null, false,
                null, null, null, null, null, null, "persistent failure"));

            var result = await NewRunner(db, src, maxRetry: 3).RunAsync(v);

            result.Outcome.Should().Be(TranscriptIngestOutcome.Parked);
            var saved = db.Videos.Single();
            saved.Status.Should().Be(VideoStatus.Discovered); // status doesn't change on park
            saved.Parked.Should().BeTrue();
            saved.ParkedAt.Should().NotBeNull();
            saved.RetryCount.Should().Be(3);
            saved.NextAttemptAt.Should().BeNull();
            db.Activity.Single().Outcome.Should().Be("parked");
        }

        [Fact]
        public async Task Backoff_doubles_each_retry_and_caps_at_one_hour()
        {
            var (conn, db) = NewDb(); using var _ = conn; using var __ = db;
            var ch = SeedChannel(db);

            // High max so we can step through 7 failures without parking.
            for (var prev = 0; prev < 7; prev++)
            {
                var v = SeedVideo(db, ch.Id, ytId: $"yt_step_{prev}");
                v.RetryCount = prev;
                db.SaveChanges();

                var src = new FakeTranscriptSource();
                src.Responses.Enqueue(new Transcript(false, null, null, false,
                    null, null, null, null, null, null, "fail"));

                var before = DateTime.UtcNow;
                await NewRunner(db, src, maxRetry: 99).RunAsync(v);

                var saved = db.Videos.Single(x => x.YoutubeVideoId == $"yt_step_{prev}");
                var actual = (saved.NextAttemptAt!.Value - before).TotalSeconds;

                // 60 * 2^(prev) seconds, capped at 3600.
                var expected = Math.Min(60.0 * Math.Pow(2, prev), 3600);
                actual.Should().BeApproximately(expected, expected * 0.1 + 5);
            }
        }

        [Fact]
        public async Task Thrown_exception_is_treated_as_transient_failure()
        {
            var (conn, db) = NewDb(); using var _ = conn; using var __ = db;
            var ch = SeedChannel(db); var v = SeedVideo(db, ch.Id);

            var src = new FakeTranscriptSource { ThrowOnNext = new InvalidOperationException("boom") };

            var result = await NewRunner(db, src).RunAsync(v);

            result.Outcome.Should().Be(TranscriptIngestOutcome.Retry);
            db.Videos.Single().LastError.Should().Be("boom");
        }

        private sealed class FakeTranscriptSource : ITranscriptSource
        {
            public Queue<Transcript> Responses { get; } = new();
            public Exception? ThrowOnNext { get; set; }

            public Task<Transcript> FetchAsync(string videoId, CancellationToken ct = default)
            {
                if (ThrowOnNext is not null)
                {
                    var ex = ThrowOnNext;
                    ThrowOnNext = null;
                    throw ex;
                }
                return Task.FromResult(Responses.Dequeue());
            }
        }
    }
    ```
- **PATTERN**: `ChannelDiscoveryRunnerTests.cs` — same `NewDb` shape, same `using var _ = conn; using var __ = db;` lifetime trick, same `Options.Create(...)` + `NullLogger<...>.Instance` runner construction. xUnit + FluentAssertions are already pulled in by `SkipWatch.Tests/Usings.cs`.
- **IMPORTS**: as listed in the skeleton.
- **GOTCHA**: `Database.Migrate()` applies the FTS5 raw-SQL migration too; the in-memory SQLite connection MUST stay open for the test's lifetime — that's what `using var _ = conn;` ensures.
- **GOTCHA**: the backoff assertion uses `BeApproximately(expected, expected * 0.1 + 5)` to absorb scheduler jitter on slow CI runners. The cap test (last iteration `prev=6`: 60 × 64 = 3840s → clamped to 3600s) MUST clamp; if the assertion fails on that iteration, the cap was missed.
- **VALIDATE**: `dotnet test SkipWatch.slnx --nologo --filter "FullyQualifiedName~TranscriptIngestRunnerTests"` exits 0 with all seven tests passing **and** `dotnet test SkipWatch.slnx --nologo` (full suite) exits 0 to confirm no Phase 0/1/1b regressions.

#### Task 7: Commit, push, and open PR

After every prior task's VALIDATE has passed, ship the phase.

- **IMPLEMENT**:
  - Confirm the branch is `phase-2-transcript-worker` (create from `master` if not: `git checkout -b phase-2-transcript-worker master`).
  - `git add -A` and commit any uncommitted changes from earlier tasks with a single message summarizing the phase: `feat: phase 2 transcript worker (Q1 Apify drain)`.
  - `git push -u origin phase-2-transcript-worker`.
  - Open the PR with the `gh` CLI:
    - **Title**: `Phase 2: Transcript worker (Q1: Apify)`
    - **Body**: copy the ACCEPTANCE CRITERIA section as a checklist with each box checked off, followed by a `## Notes` section listing the assumptions in the NOTES section of this plan plus anything new that came up during implementation.
- **GOTCHA**: `gh` must be installed and authenticated (used in Phase 1 / 1b — assume present). If absent, the operator runs `gh auth login` before this task.
- **GOTCHA**: do NOT include the removal of `docs/progress.md` or stale phase-1 task notes if any reappear — Phase 1 cleaned those up; the phase-2 branch should contain only Phase 2 changes.
- **VALIDATE**: `gh pr view --json number,title,state,headRefName` returns the new PR with `"state": "OPEN"` and `"headRefName": "phase-2-transcript-worker"`.

---

## TESTING STRATEGY

The project uses **xUnit + FluentAssertions** (per `SkipWatch.Tests/Usings.cs` and existing test files). In-memory SQLite (`Microsoft.Data.Sqlite` `Data Source=:memory:` with `Database.Migrate()`) substitutes for a real DB — it exercises the actual EF model, indexes, and FTS5 raw-SQL migration.

### Unit Tests

Seven tests on `TranscriptIngestRunner` covering:

- Successful transcript → `Transcribed` status, fields written, retry state cleared, `Activity.Outcome = "ok"`.
- No-captions success → `NoTranscript` terminal status, cheap fields still overwritten, `Activity.Outcome = "no_transcript"`.
- Cheap-field non-overwrite when Apify returns nulls (preserve existing values).
- Transient failure → `RetryCount = 1`, `LastError` set, `NextAttemptAt ≈ now + 60s`, `Activity.Outcome = "fail"`.
- Failure at `MaxRetryAttempts` → `Parked = true`, `ParkedAt` set, `NextAttemptAt = null`, `Activity.Outcome = "parked"`.
- Backoff schedule across 7 retries — doubles each step, caps at 1h.
- Thrown exception in `FetchAsync` → classified as transient failure (not surfaced).

### Integration Tests

`HealthEndpointTests` already verifies the host wires up; the worker DI registration and `TranscriptWorker` startup are implicitly covered when the test host boots without DI errors. No new integration test is required for this phase per PRD §6 Phase 2's scope (no UI surface, no API endpoint — the debug endpoint is intentionally removed).

### Edge Cases

Covered by the unit tests above:

- Apify returns success but no usable subtitle (`HasTranscript = false`).
- Apify returns success with sparse cheap fields (some null).
- Repeated failures correctly compound the backoff.
- The retry counter resets on successful transition (verified by the success tests asserting `RetryCount = 0`).

Not separately tested:

- Worker loop tick semantics (idle sleep, scope-per-tick) — covered by the runner's correctness + manual smoke (Level 4) below.
- Concurrent ticks — MVP runs serially; concurrency is a v2 concern.

---

## VALIDATION COMMANDS

The project's primary tooling is `dotnet` (no `pyproject.toml`, no `package.json`). Solution file is `SkipWatch.slnx`.

Execute every command to ensure zero regressions and 100% phase correctness.

### Level 1: Build & Style

```bash
dotnet build SkipWatch.slnx --nologo /warnaserror
```

The repo's `Directory.Build.props` already sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, so `/warnaserror` is belt-and-braces. No separate `dotnet format` step is enforced by CI today (`.github/workflows/ci.yml` runs `dotnet build` + `dotnet test`); the style gate is the build itself.

**Expected**: exit code 0, zero warnings.

### Level 2: Unit Tests

```bash
dotnet test SkipWatch.slnx --nologo
```

**Expected**: every test in `SkipWatch.Tests` passes, including the new `TranscriptIngestRunnerTests` and the unchanged Phase 0/1/1b tests.

### Level 3: Integration Tests

Same command — `HealthEndpointTests` runs as part of the full suite and proves the host (with the new hosted service registered) boots cleanly.

### Level 4: Manual Validation

Single-shot host-boot smoke that verifies the worker registers, the host comes up, `/health` responds, and shutdown completes without unhandled exceptions. **Skip if running headless without an `Apify:Token` configured** — the worker will log warnings but the host still boots.

```bash
set -e
dotnet build SkipWatch.slnx --nologo /warnaserror
dotnet run --project SkipWatch/SkipWatch.csproj --no-build --launch-profile http &
SERVER_PID=$!
trap 'kill $SERVER_PID 2>/dev/null || true; wait $SERVER_PID 2>/dev/null || true' EXIT
ok=1
for i in $(seq 1 30); do
  if curl -fsS http://localhost:5000/health > /dev/null 2>&1 \
     || curl -fsS http://localhost:5028/health > /dev/null 2>&1; then
    ok=0; break
  fi
  sleep 1
done
exit $ok
```

The launch profile `http` defaults to port 5028 in `Properties/launchSettings.json`; the loop accepts either common dev port. The trap guarantees teardown on any exit path.

### Level 5: Additional Validation (Optional)

None for this phase.

---

## ACCEPTANCE CRITERIA

- [ ] `TranscriptWorkerSettings` exists with `Concurrency = 1` and `IdlePollSeconds = 10` defaults; `appsettings.json` carries the same defaults under a top-level `TranscriptWorker` section.
- [ ] `ITranscriptIngestRunner` + `TranscriptIngestRunner` exist in `SkipWatch.Core/Services/Transcripts/`; the runner never throws across its boundary except for caller-driven `OperationCanceledException`.
- [ ] `TranscriptWorker : BackgroundService` exists in `SkipWatch/Services/Workers/`; it picks one row per tick using a query that hits `idx_videos_q_transcript`, sleeps `IdlePollSeconds` only when the queue is empty, and honors `stoppingToken` on every await.
- [ ] Successful transcript transitions the row to `VideoStatus.Transcribed`, sets `TranscriptText` / `TranscriptLang` / `HasTranscript = true` / `TranscribedAt`, overwrites the cheap-field columns when Apify supplies non-null values, and resets `RetryCount = 0` / `LastError = null` / `NextAttemptAt = null`.
- [ ] No-captions success transitions the row to `VideoStatus.NoTranscript` (terminal); `TranscriptText` stays null; the cheap-field overwrite still runs.
- [ ] Transient failure leaves `Status = Discovered`, increments `RetryCount`, sets `LastError`, and sets `NextAttemptAt = now + min(60s × 2^(RetryCount-1), 1h)`.
- [ ] Once `RetryCount >= DiscoverySettings.MaxRetryAttempts`, the row is marked `Parked = true` with `ParkedAt = now()` and `NextAttemptAt = null`.
- [ ] One `ActivityEntry` is written per processed row with `Kind = "transcript"`, `RefId = videoId`, and `Outcome ∈ {"ok","no_transcript","fail","parked"}`.
- [ ] DI: `ITranscriptIngestRunner` registered scoped; `TranscriptWorker` registered as a hosted service; `TranscriptWorkerSettings` bound from configuration.
- [ ] `/debug/transcript/{videoId}` endpoint is removed from `Program.cs`; `/health` still responds.
- [ ] All seven new unit tests pass; full `dotnet test` suite passes with no regressions.
- [ ] `dotnet build SkipWatch.slnx --nologo /warnaserror` exits 0.

---

## COMPLETION CHECKLIST

- [ ] All tasks completed in order
- [ ] Each task validation passed immediately
- [ ] All validation commands executed successfully:
  - [ ] Level 1: `dotnet build SkipWatch.slnx --nologo /warnaserror`
  - [ ] Level 2: `dotnet test SkipWatch.slnx --nologo`
  - [ ] Level 3: same (full suite)
  - [ ] Level 4: manual host-boot smoke (skipped if running headless without an Apify token)
- [ ] Full test suite passes (unit + integration)
- [ ] No build warnings (treated as errors)
- [ ] All acceptance criteria met
- [ ] Code reviewed for quality and maintainability
- [ ] Branch `phase-2-transcript-worker` pushed and PR opened with title `Phase 2: Transcript worker (Q1: Apify)`

---

## NOTES

**Decision: keep concurrency fixed at 1 in MVP, surface the setting anyway.** PRD §6 Phase 2 explicitly defaults `TRANSCRIPT_WORKER_CONCURRENCY = 1`. Implementing a worker pool now (e.g., `Parallel.ForEachAsync` or a `Channel<T>`-backed dispatcher) would add real complexity to honor row-level locking, scope lifetime, and error attribution. The setting is stored on `TranscriptWorkerSettings.Concurrency` so Phase 7's settings page can render it (read-only is fine for MVP); v2 can wire actual parallelism behind the same knob without changing the config schema.

**Decision: separate `TranscriptWorkerSettings` from `DiscoverySettings`.** `MaxRetryAttempts` is a cross-cutting retry knob (used by the channel runner today and by every future worker), so it stays on `DiscoverySettings` despite the name. The transcript-worker-specific `Concurrency` and `IdlePollSeconds` belong on their own type bound to `TranscriptWorker:` in config — keeps each settings class small and lets Phase 3's summary worker introduce its own `SummaryWorkerSettings` symmetrically without one giant blob.

**Decision: classify all `Transcript.Success = false` outcomes as transient.** The runner does not discriminate between "Apify 502" (network) and "Apify 401" (bad credentials). PRD §6 Phase 2 defines a single retry/park path; the same exponential backoff plus the park threshold catch every kind of failure. If credentials are wrong, the row parks at `MaxRetryAttempts` (default 3) — three Apify calls (~$0.015) to surface the misconfig is acceptable. Phase 7's settings page surfaces credentials, and a future circuit breaker (PRD §8) can pause the queue across rows when credentials look wrong globally.

**Decision: preserve existing cheap-field values when Apify returns null for that field.** The Data API at discovery time gives us best-effort `DurationSeconds` / `ViewCount` / etc.; Apify's later run usually returns fresher values, but if a particular field is null (e.g. an old video with no comment count), keeping the discovery-time value beats nulling it out. The PRD says "overwrites the cheap-field columns with Apify's richer values" — interpreted as "overwrite when richer is available", which the per-field `if (... is not null)` guard implements.

**Decision: write `ActivityEntry.Outcome = "no_transcript"` rather than `"ok"` for the terminal-no-captions path.** The `ActivityEntry` doc-comment lists `'ok' | 'fail' | 'skipped_short' | 'skipped_too_long' | 'parked'` as canonical outcomes. `no_transcript` is a phase-2-specific terminal that doesn't fit those buckets — adding it as a new value follows the same pattern as `skipped_short`/`skipped_too_long` (which are phase-1-specific) and gives the eventual settings/logs pane (Phase 7) a clear filter.

**Decision: `TranscriptWorker` lives under `SkipWatch/Services/Workers/`, not `SkipWatch/Services/Discovery/`.** The `Discovery/` folder is reserved for the cron-driven discovery round and its companions; queue-draining workers (this one, Phase 3 summary, Phase 5 wiki) compose under `Workers/`. Phase 3 will create `SummaryWorker.cs` in the same folder.

**Quota / cost sanity-check (defaults applied).** Apify is billed per run (~$0.005 per video). The worker's max throughput is bounded by discovery: at the Phase 1/1b round caps (5 channels × ≤10 new videos/round + 2 topics × ≤20 IDs × 48 rounds/day, with the 24h fairness gate making sustained-state much lower), expect ~$5-15/month for a 100-channel follow list (PRD §6 Phase 2 cost paragraph). Phase 7's settings page surfaces a running spend estimate; until then, the operator monitors the Apify console.

**Removed: `/debug/transcript/{videoId}`.** Existed only to validate the Apify integration before a worker existed (per its inline `// H6 validation endpoint —` comment). With the worker landed, calling the endpoint would race the worker against itself for the same video. PRD §6 Phase 2 explicitly marked it for removal once the worker was wired up.
