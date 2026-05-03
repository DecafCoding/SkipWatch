# SkipWatch — Development Plan

> Skip or watch. A single-user, locally installed YouTube triage library. Deterministic ingestion, LLM-summarized decision-support cards, three first-class actions per video: **Library**, **Project**, **Pass**. Projects accumulate a per-project **wiki** — Markdown pages compiled by the LLM that grow as you add videos.

This document is the build plan for the first few months of work. It captures the chosen stack, related projects we drew from, the data model, an MVP scope, and a v2 backlog. It is meant to live alongside the code and be updated as decisions change.

---

## 1. Goals and non-goals (recap from `Description.md`)

**Goals.** Calm, triaged, searchable library of videos from channels the user follows. Local-first, single-user, no accounts, no cloud sync, no notifications. Every new video lands as a card with a 1–2 paragraph decision-support summary that leads with the subject matter, and a clean three-way decision: file it into a Library, attach it to a Project, or Pass. Projects are research collections that compound — each one maintains its own LLM-compiled wiki of Markdown pages with timestamped citations back to the videos that taught them.

**Explicit non-goals.** Not a personal AI assistant. Not a multi-source aggregator (no RSS-of-everything, no podcasts, no papers — that is a different product). Not a Telegram bot or mobile app. Not a general-purpose chat product — chat is always scoped to the user's own video library or a single Project.

These constraints matter because they let us drop a lot of architecture: no auth, no multi-tenant tables, no push infrastructure, no notification routing, no sync engine. The whole thing is one local process plus a local LLM.

---

## 2. Related work and where SkipWatch fits

A scan of comparable open-source and commercial projects, with the angle each one takes and what SkipWatch borrows or rejects:

| Project | What it is | What SkipWatch borrows | Where SkipWatch differs |
|---|---|---|---|
| [Tube Archivist](https://github.com/tubearchivist/tubearchivist) | Self-hosted YouTube media server: subscribe, download, organize | Channel-following ingestion model; SQLite-as-system-of-record idea | Tube Archivist hoards video files; SkipWatch keeps only metadata + summaries and stays small |
| [FreeTube](https://freetubeapp.io/) | Privacy-respecting YouTube desktop client with local subscription state | Local-only state philosophy; storing subscriptions as data, not as a Google account | FreeTube is a player; SkipWatch is a triage queue with no built-in playback |
| [ytsm](https://github.com/chibicitiberiu/ytsm) / [ytsms](https://github.com/MDeLuise/ytsms) | Self-hosted YouTube subscription managers with optional download | Polling cadence and channel-add UX | They focus on auto-download; SkipWatch focuses on summarize-then-decide |
| [AskTube](https://github.com/jonaskahn/asktube) | Local YouTube summarizer + RAG Q&A, supports Ollama | Ollama integration pattern; transcript-first pipeline | AskTube is per-video Q&A using embeddings; SkipWatch is library-first triage with per-Project compiled wikis instead of vector retrieval |
| [Samurize / Bricolage build notes](https://bricolage.io/build-notes-ai-powered-youtube-summarizer/) | Local-first AI YouTube summarizer prototype | Map-reduce summarization on long transcripts; chunk-then-condense pattern | Built on ElectricSQL for sync — overkill for a single-user product |
| [Tomash Corner: Melting down Watch Later with LLMs](https://tomash.wrug.eu/blog/2024/07/15/llm-youtube-watchlater/) | Personal blog on triaging watch-later via LLM summaries | Validation that summarize-then-triage actually works (60+ → 18 in one sitting) | One-off script, not an app |
| [Karpathy's LLM Wiki pattern](https://gist.github.com/rohitg00/2067ab416f7bbe447c1977edaaa681e2) | Three-folder Markdown knowledge base maintained by an LLM (raw / wiki / schema) | The whole per-Project wiki concept: compiled Markdown with cross-refs, no vector DB, audit-trail-by-design | SkipWatch scopes the wiki *per Project* (not global) so the ~150-200 page per-wiki ceiling never bites; raw transcripts live in SQLite, not a `raw/` folder |

**Where SkipWatch lands.** None of the existing tools combine all four pillars at once: channel-following ingestion, decision-support summaries (not Q&A snippets), a triage-first UI with explicit Pass, and per-Project compiled wikis that compound across the videos you keep. That is the gap.

---

## 3. Tech stack (chosen)

The user's call: **C# / .NET backend + web UI**, **Ollama** for local LLM, MVP scoped to **core triage plus per-Project wiki compilation**. No vector database, no embeddings — all retrieval is FTS5 (library-wide) or compiled Markdown (per-Project).

- **Code organization:** **Vertical Slice with shared services** (mirrors the prior TargetBrowse project the user is harvesting from). Each feature lives under `SkipWatch/Features/<Feature>/{Components, Services, Models, Utilities}` and is self-contained — its Razor components, `IXService` + `XService`, DTO records, and feature-local helpers all sit together. Cross-cutting concerns (YouTube API, quota manager, transcript source, theme, message center, DbContext, entities) live in shared layers — `SkipWatch/Services/` (UI-adjacent: theme, message center) and `SkipWatch.Core/Services/` (server-side: YouTube, utilities, transcripts). The two-project App + Core split keeps `SkipWatch.Core` UI-free so future workers (Phases 4–5) can reference it without dragging in Blazor. **No per-feature repository layer** — `XService` talks to `SkipWatchDbContext` directly; EF Core is the persistence abstraction. **No CQRS / MediatR / AutoMapper** — services have explicit methods, manual `ToDto` mapping, and result-type records (e.g. `AddChannelResult` with explicit success/duplicate/quota branches) for expected outcomes instead of exception-driven control flow.
- **Language / runtime:** C# on .NET 10 (current; matches the existing SkipWatch skeleton). Nullable reference types on, `<TreatWarningsAsErrors>` enabled via a `Directory.Build.props` at the repo root.
- **App framework:** **Blazor Server** on ASP.NET Core (`WebApplication.CreateBuilder` + `AddRazorComponents().AddInteractiveServerComponents()`). UI is `.razor` components rendered server-side and pushed to the browser over a SignalR circuit. No separate REST API — components inject services directly. Async end-to-end. A handful of minimal-API endpoints stay for genuine non-UI surfaces (health check, raw wiki Markdown file streaming if the user wants to deep-link a page, future MCP/automation hooks).
- **Job scheduler:** In-process `IHostedService` plus `PeriodicTimer` for the polling job (`collection-round`); long-running `BackgroundService` instances for each of the three workers. No Quartz / Hangfire / external broker — for a single-user app the built-in hosted-service abstractions are sufficient and one less moving part. Can be swapped to Quartz.NET later if the scheduling story gets more complex.
- **Database:** SQLite with FTS5, accessed via **EF Core 10** on the `Microsoft.EntityFrameworkCore.Sqlite` provider. Code-first — entities live in `SkipWatch.Core/Entities/`, configured via fluent API in `SkipWatchDbContext.OnModelCreating`. Single file on disk. No vector extension. Queries that benefit from raw SQL (FTS5 `MATCH`, snippet ranking) drop down via `context.Database.SqlQuery<>()` rather than going through LINQ.
- **Migrations:** **EF Core migrations** (`dotnet ef migrations add <Name>` → `.cs` files checked into the repo). Applied at startup via `context.Database.Migrate()`. The FTS5 virtual table, its three sync triggers, and the partial indexes (`HasFilter`) are added inside hand-edited migrations using `migrationBuilder.Sql(...)` since EF has no first-class model for FTS5; the regular tables and indexes are generated from the model.
- **Wiki storage:** Plain Markdown files on disk under `<data-dir>/wiki/<project-slug>/`. Sibling to `skipwatch.db`. One folder per Project, each containing `index.md`, `SCHEMA.md`, and `pages/*.md`. Compiled and rewritten by the wiki worker; readable and editable by the user with any text editor.
- **LLM:** Local model — default `Qwen3-Coder-30B-A3B-Instruct-Q4_K_M.gguf` (Qwen3-Coder 30B MoE, ~3B active params, Q4_K_M quantization). Loaded via Ollama (custom Modelfile) or directly via llama.cpp/llama-server. Talked to from C# via **OllamaSharp** (or a thin `HttpClient` wrapper around Ollama's `/api/generate` and `/api/chat`). The model name is a **config setting**, not hardcoded — an `ISummarizer` interface accepts any compatible model so users can swap in a smaller GGUF on lower-end hardware or a larger one if they have the VRAM. Used for both per-video short summaries (cheap, context-light) and per-Project wiki ingest passes (expensive, context-heavy). Microsoft.SemanticKernel is intentionally avoided — it adds abstraction layers we don't need for two well-defined LLM call sites.
- **Discovery (new-video listing):** YouTube Data API v3 via the official **`Google.Apis.YouTube.v3`** NuGet client, round-based. `channels.list` once at add-time to grab the uploads-playlist ID (1 unit, stored on the row). On each scheduler tick, `playlistItems.list` against the uploads playlist (1 unit/page of 50) for the channels picked this round, then `videos.list` (1 unit/page of 50) on the new IDs to read `duration` and basic stats *before* deciding whether to spend Apify on them. RSS feed is rejected: only the last 15 videos and the maintainers note YouTube can change the format without warning — Data API is the reliable surface. The discovery surface is implemented in `SkipWatch.Core/Services/YouTube/` as `YouTubeApiService` (round mechanics + channel resolver) plus `DurationParser` and a `Models/YouTubeApiSettings` record bound from the `YouTube:` configuration section.
- **Quota tracking:** **`YouTubeQuotaManager`** (singleton, harvested from TargetBrowse) is the single seam in front of every Data API call. It persists daily usage and active reservations to a JSON file under `~/.skipwatch/` so quota survives process restarts within a UTC day, gates concurrent calls through a `SemaphoreSlim` sized by `YouTubeApiSettings.MaxConcurrentRequests`, and raises `QuotaThresholdReached` / `QuotaExhausted` events that the message-center service surfaces in the UI. Refuses calls once the configurable ceiling (`CeilingUnits`, default 9000) is hit, deferring further discovery until UTC rollover.
- **Transcript + rich metadata:** Apify [`streamers/youtube-scraper`](https://apify.com/streamers/youtube-scraper) actor, called per video with `includeSubtitles=true` (plain-text format with timestamps preserved per cue). Apify has no official .NET SDK, so a thin `HttpClient`-backed implementation of `ITranscriptSource` (`ApifyTranscriptSource`, registered via `IHttpClientFactory`) hits the run-actor REST endpoint synchronously (`run-sync-get-dataset-items`) — single request per video, dataset items returned inline. One actor run per new video. The response gives us the timestamped transcript, plus the description (`text`), `duration`, `viewCount`, `likes`, `commentsCount`, channel fields, and `thumbnailUrl` — so a single Apify call replaces a separate transcript fetch *and* a metadata-enrichment call. Pricing is roughly $0.005/video, billed per result. Tunables (`Token`, `RunTimeoutSeconds`, `PreferredLanguage`, `PreferAutoGenerated`) bind from the `Apify:` configuration section into an `ApifySettings` record.
- **Configuration:** `Microsoft.Extensions.Configuration` with the Options pattern. Layered: `appsettings.json` for defaults shipped with the binary, then **user-secrets** during development (`dotnet user-secrets set "YouTube:ApiKey" "..."`, auto-loaded by `WebApplication.CreateBuilder` when `<UserSecretsId>` is set in the csproj), then standard environment variables for the shipped form on the user's machine. The two settings sections (`YouTube:`, `Apify:`) bind to `YouTubeApiSettings` and `ApifySettings` records via `Configure<T>(...)`. Runtime knobs (cron, caps, durations, concurrency, model names) live in the same configuration chain alongside credentials — no separate TOML/YAML layer.
- **UI:** Blazor Server `.razor` components. Styling via **Bootstrap 5** referenced from the **jsDelivr CDN** with SRI hashes (matching the TargetBrowse pattern: `bootstrap@5.3.7` CSS, `bootstrap-icons@1.13.1`, `bootstrap@5.3.8` JS bundle), plus a small handwritten CSS layer (`wwwroot/app.css` + `wwwroot/css/components/`) for the dark theme, video cards, top menu, and left sidebar — all harvested from the prior TargetBrowse project where this shell was already built and is the part the user wants to preserve. No Bootstrap files vendored in the repo, no Node, no npm, no MSBuild CSS step. Markdown rendering via **Markdig**. A `MessageCenterService` (also harvested) backs a sidebar message panel for success/error/quota notifications. Component library is otherwise hand-rolled — the UI surface is small (Dashboard, Channels, Topics, Project, Settings) so MudBlazor and similar kits add more than they remove. SignalR is the transport for the render circuit — already in the box, no extra wiring.
- **Packaging:** `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` produces a single `SkipWatch.exe` (and equivalents for `osx-arm64` / `linux-x64`). The compiled `app.css` and any static assets are embedded under `wwwroot/`. The PowerShell launcher (`scripts/launch.ps1`) ensures Ollama is running, pulls the configured model if missing, then starts the binary (which runs EF Core migrations on its own at startup via `context.Database.Migrate()`) and opens the browser at `http://localhost:7860`.

**Why these choices.** Blazor Server + hosted services keeps the entire system in one process and one language for a single user — no broker, no extra daemon, no JS toolchain, no REST/OpenAPI/codegen layer between UI and services. `Program.cs` wires `.razor` components and `BackgroundService`s into the same DI container; a Dashboard component injects `IVideoQueries` and renders cards directly. The render circuit gives us push updates as a freebie — when a worker advances a video to `ready`, it raises an event the Dashboard component is subscribed to, the affected card re-renders in place, and there is no JSON serialization or HTTP round-trip in the path. For a local single-user app this is the right shape: the "REST API + SPA" pattern exists to cross a trust/process boundary that does not exist here. SQLite with FTS5 covers lexical search inside one file with no external service; EF Core 10 handles the regular tables while a hand-edited migration drops to raw SQL for the FTS5 virtual table and triggers. The Karpathy-style per-Project wiki replaces the embedding/vector-search story entirely: instead of paying for embeddings on every video and reconstructing context at query time, we pay LLM cost *only* for videos the user has decided are worth it (added to a Project), and the resulting compiled Markdown is a permanent, auditable, hand-editable knowledge surface. Per-Project scoping keeps each wiki under the ~150-200 page ceiling where the index fits in a single context window. Qwen3-Coder-30B (MoE, ~3B active) gives us frontier-tier instruction-following at small-model inference cost; Q4_K_M is the standard quality/size tradeoff for that class. Splitting the data path so YouTube Data API does discovery and Apify does the heavy lift (transcripts + rich metadata) means each tool does what it is best at: Data API is cheap for cheap things, Apify handles the mess of YouTube subtitle endpoints with a Whisper fallback baked in. The C# / .NET choice over Python: stronger static typing for a workload with several long-lived background loops + many small DTOs, single-file self-contained publish makes Windows distribution clean (no Python interpreter for the user to manage), and `BackgroundService` + `Channel<T>` + `PeriodicTimer` are mature primitives for exactly the producer/consumer queue topology this app has.

---

## 4. Architecture

```
                                +---------------------------+
                                |  Browser (Blazor Server)  |
                                |  - Dashboard              |
                                |  - Search                 |
                                |  - Project + Wiki         |
                                |  - Settings               |
                                +-------------+-------------+
                                              |
                                              | SignalR (WebSocket)
                                              | — Blazor render circuit
                                              v
+----------------------+         +---------------------------+
| YouTube Data API v3  |<--------+  ASP.NET Core host        |
| (discovery only)     |         |  - Blazor Server          |
+----------------------+         |    + .razor pages         |
                                 |    + service injection    |
+----------------------+         |  - Workers (next column)  |
| Apify                |<--+     |  - SQLite + filesystem    |
|  streamers/youtube-  |   |     |                           |
|  scraper actor       |   |     |                           |
+----------------------+   |     +-------------+-------------+
                           |                   |
                           |                   v
+----------------------+   |     +---------------------------+
| Local Ollama runtime |<--+--+  |  Workers (in-process)     |
|  - Qwen3-Coder-30B   |   |  |  |                           |
|    (summary + wiki)  |   |  |  |  CollectionRoundService   |
+----------------------+   |  |  |   IHostedService +        |
                           |  |  |   PeriodicTimer, */30 min |
                           |  |  |   ─ Data API discovery    |
                           |  |  |   ─ writes 'discovered'   |
                           |  |  |                           |
                           +--+--+   TranscriptWorker        |
                              |  |   BackgroundService       |
                              |  |   ─ Q1: discovered →      |
                              |  |       transcribed         |
                              |  |   ─ Apify per video       |
                              |  |                           |
                              +--+   SummaryWorker           |
                                 |   BackgroundService       |
                                 |   ─ Q2: transcribed →     |
                                 |       ready               |
                                 |   ─ LLM short summary     |
                                 |     only (no analysis)    |
                                 |                           |
                                 |   WikiWorker              |
                                 |   BackgroundService       |
                                 |   ─ Q3: project_wiki_jobs |
                                 |   ─ triggered eagerly by  |
                                 |     add/remove from a     |
                                 |     Project               |
                                 |   ─ reads transcript +    |
                                 |     summary + project's   |
                                 |     index.md, rewrites    |
                                 |     affected wiki pages   |
                                 +-------------+-------------+
                                               |
                                               +--> SQLite (skipwatch.db)
                                               |     - channels, videos
                                               |     - libraries, projects
                                               |     - video_projects
                                               |     - project_wiki_jobs
                                               |     - video_fts (FTS5)
                                               |
                                               +--> Filesystem (<data-dir>/wiki/)
                                                     - <project-slug>/index.md
                                                     - <project-slug>/SCHEMA.md
                                                     - <project-slug>/pages/*.md
```

Four independent workers, all in-process. UI updates ride the existing Blazor Server SignalR circuit — workers publish phase-change events through an in-process `IObservable<JobEvent>` (or `Channel<JobEvent>` fan-out), Blazor components subscribe and call `InvokeAsync(StateHasChanged)`. Cards light up as their phase advances; wiki-update banners flip in place on Project pages. No separate SSE endpoint, no `/api/jobs`, no client-side polling — the render circuit *is* the push channel. Consistent with the "no notifications" rule: it never leaves the browser tab.

**`CollectionRoundService`** is an `IHostedService` driven by a `PeriodicTimer` set to 30 minutes (the cron expression `*/30 * * * *` is the user-facing knob — translated to a `TimeSpan` at startup; if a richer cron expression is configured, **NCrontab** computes the next fire time). Each round picks up to **5 channels** not visited in the last 24 hours and writes any new uploads as `status='discovered'` rows. On the same tick the round also picks up to **2 topics** not visited in the last 24 hours and runs `search.list` against each (Phase 1b — see §6); topic-found videos land in the same `discovered` status, with provenance recorded in `topic_videos`. The round does **only** discovery — it never blocks on Apify or the LLM, so a slow LLM never pushes the next round past its tick.

**`TranscriptWorker`** is a `BackgroundService` running a continuous loop that pulls one `discovered` row at a time, calls the Apify actor, writes the timestamped transcript, and advances the row to `transcribed`.

**`SummaryWorker`** is a `BackgroundService` that pulls one `transcribed` row at a time, runs the LLM short-summary pass, writes `summary_md` + `decision_signal`, and advances to `ready`. **No long-form analysis is produced at this stage** — analysis only happens later, in the wiki worker, scoped to a specific Project.

**`WikiWorker`** is a `BackgroundService` that drains the `project_wiki_jobs` queue. Jobs are enqueued **eagerly** when a video is added to or removed from a Project. The worker reads the affected Project's `index.md`, decides which wiki pages need updating (or creating), reads the video's transcript + short summary, and rewrites those pages with timestamped citations. The result is the LLM's per-Project analysis of the video, materialized as Markdown.

Each queue retries independently with its own backoff, so an Apify outage never burns through LLM retries, a slow LLM never re-spends Apify dollars, and a flaky wiki pass never blocks the user from triaging new videos.

---

## 5. Data model

A small, deliberately boring schema. Status is denormalized onto `Video` so the dashboard query is one fast scan. The wiki itself lives on the filesystem, not the DB — only the *job queue* for wiki updates is an entity. Code-first: the entities below live in `SkipWatch.Core/Entities/` and are wired into `SkipWatchDbContext`. EF Core 10 + the SQLite provider generates the SQL; conventions handle most mappings, with `OnModelCreating` covering uniqueness, composite keys, partial-index filters, and string-conversion of enums. The FTS5 virtual table and its sync triggers are emitted by a hand-edited migration via `migrationBuilder.Sql(...)` since EF has no model-level support for FTS5.

```csharp
// Entities/Channel.cs — channels the user follows
public class Channel
{
    public int Id { get; set; }
    public required string YoutubeChannelId { get; set; }
    public required string UploadsPlaylistId { get; set; }   // captured once at add-time
    public required string Title { get; set; }
    public string? Handle { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckAt { get; set; }               // updated every visit, success OR failure
    public string? LastCheckError { get; set; }

    public ICollection<Video> Videos { get; set; } = new List<Video>();
}

// Entities/Video.cs — one row per video we've ingested
public class Video
{
    public int Id { get; set; }
    public required string YoutubeVideoId { get; set; }
    public int ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;
    public required string Title { get; set; }

    // Populated at discovery time (Data API: playlistItems.list + videos.list)
    public DateTime PublishedAt { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public long? ViewCount { get; set; }
    public long? LikeCount { get; set; }
    public long? CommentsCount { get; set; }

    // Raw YouTube description, filled in by the transcript worker (Apify `text`
    // field). NOT what the dashboard card shows — that's `SummaryMd`. The
    // description is surfaced on the full video-details view alongside the summary.
    public string? Description { get; set; }

    // Pipeline + triage state — one property drives everything.
    // Stored as TEXT (HasConversion<string>) so the DB stays human-readable.
    public VideoStatus Status { get; set; } = VideoStatus.Discovered;

    // Per-phase retry state. RetryCount resets to 0 on every status transition;
    // it counts attempts in the *current* phase only.
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptAt { get; set; }             // exponential backoff gate
    public bool Parked { get; set; }                         // true once RetryCount hits MAX_RETRY_ATTEMPTS
    public DateTime? ParkedAt { get; set; }

    // Triage + transcript + summary
    public int? LibraryId { get; set; }
    public Library? Library { get; set; }
    public string? TranscriptText { get; set; }              // timestamped: each line prefixed "[mm:ss] ..."
    public string? TranscriptLang { get; set; }              // e.g. "en"
    public bool HasTranscript { get; set; }
    public DateTime? TranscribedAt { get; set; }
    public string? SummaryMd { get; set; }                   // 1–2 paragraph card summary (only LLM artifact at this stage)
    public DecisionSignal? DecisionSignal { get; set; }      // Watch | Skim | Skip
    public DateTime? SummarizedAt { get; set; }
    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VideoProject> VideoProjects { get; set; } = new List<VideoProject>();
}

public enum VideoStatus
{
    Discovered, Transcribed, Ready,                          // pipeline
    SkippedShort, SkippedTooLong, NoTranscript,              // filtered
    Libraried, Projected, Passed                             // triage
}

public enum DecisionSignal { Watch, Skim, Skip }

// Entities/Library.cs — consumption buckets ("Education", "Entertainment")
public class Library
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
}

// Entities/Project.cs — research collections ("AI Skills"), many videos to many
// projects. Each project owns a wiki folder on disk at <data-dir>/wiki/<Slug>/.
public class Project
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }

    // Driven by the wiki worker. 'Stale' means a job is queued but not yet
    // running (rare — debounce window).
    public ProjectWikiStatus WikiStatus { get; set; } = ProjectWikiStatus.Idle;
    public DateTime? WikiUpdatedAt { get; set; }

    public ICollection<VideoProject> VideoProjects { get; set; } = new List<VideoProject>();
}

public enum ProjectWikiStatus { Idle, Updating, Stale, Error }

// Entities/VideoProject.cs — join entity, composite PK (VideoId, ProjectId)
public class VideoProject
{
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

// Entities/ProjectWikiJob.cs — wiki ingest job queue. One row per
// (project, video, action) tuple. Eagerly enqueued by the command handlers
// when a video is added to or removed from a project. WikiWorker drains this
// queue serially per project.
public class ProjectWikiJob
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;
    public WikiJobAction Action { get; set; }                // Add | Remove
    public WikiJobStatus Status { get; set; } = WikiJobStatus.Queued;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum WikiJobAction { Add, Remove }
public enum WikiJobStatus { Queued, Running, Done, Parked }

// Entities/ActivityEntry.cs — feed for the logs pane and the source for live
// UI updates via JobEventBus.
public class ActivityEntry
{
    public int Id { get; set; }
    public required string Kind { get; set; }                // 'round_start' | 'channel_visit' | 'transcript' | 'summary' | 'wiki'
    public int? RefId { get; set; }                          // ChannelId, VideoId, or ProjectId depending on Kind
    public required string Outcome { get; set; }             // 'ok' | 'fail' | 'skipped_short' | 'skipped_too_long' | 'parked'
    public string? Detail { get; set; }
    public int? DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Entities/Topic.cs — keyword-based discovery surface, alongside followed channels.
// Harvested concept from TargetBrowse but stripped of the rating/scoring system.
// A topic feeds the same pipeline a channel does: discovery writes 'discovered'
// rows; the transcript and summary workers don't care which source produced them.
public class Topic
{
    public int Id { get; set; }
    public required string Name { get; set; }                // e.g. "Postgres internals"
    public required string Query { get; set; }              // raw YouTube search query string
    public bool Enabled { get; set; } = true;
    public int LookbackDays { get; set; } = 7;              // search.list publishedAfter window
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckAt { get; set; }
    public string? LastCheckError { get; set; }

    public ICollection<TopicVideo> TopicVideos { get; set; } = new List<TopicVideo>();
}

// Entities/TopicVideo.cs — provenance: which topic(s) discovered which video.
// Composite PK (TopicId, VideoId). A video found by both a channel and a topic
// keeps its existing Video row — TopicVideo just records the additional source.
public class TopicVideo
{
    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}
```

```csharp
// Db/SkipWatchDbContext.cs — single context, all entities, fluent config
public sealed class SkipWatchDbContext(DbContextOptions<SkipWatchDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<VideoProject> VideoProjects => Set<VideoProject>();
    public DbSet<ProjectWikiJob> ProjectWikiJobs => Set<ProjectWikiJob>();
    public DbSet<ActivityEntry> Activity => Set<ActivityEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Enums as strings — stays human-readable in the SQLite file.
        b.Entity<Video>().Property(v => v.Status).HasConversion<string>();
        b.Entity<Video>().Property(v => v.DecisionSignal).HasConversion<string>();
        b.Entity<Project>().Property(p => p.WikiStatus).HasConversion<string>();
        b.Entity<ProjectWikiJob>().Property(j => j.Action).HasConversion<string>();
        b.Entity<ProjectWikiJob>().Property(j => j.Status).HasConversion<string>();

        // Channels
        b.Entity<Channel>().HasIndex(c => c.YoutubeChannelId).IsUnique();
        b.Entity<Channel>().HasIndex(c => new { c.Enabled, c.LastCheckAt })
            .HasDatabaseName("idx_channels_round_pick");

        // Videos
        b.Entity<Video>().HasIndex(v => v.YoutubeVideoId).IsUnique();
        b.Entity<Video>().HasIndex(v => new { v.Status, v.PublishedAt })
            .HasDatabaseName("idx_videos_status_published")
            .IsDescending(false, true);
        b.Entity<Video>().HasIndex(v => new { v.ChannelId, v.PublishedAt })
            .HasDatabaseName("idx_videos_channel")
            .IsDescending(false, true);

        // Partial indexes that drive the two pipeline worker queues. Both stay
        // tiny because they only contain rows actively waiting on a phase.
        // Workers also filter on NextAttemptAt <= now() to honor exponential backoff.
        b.Entity<Video>()
            .HasIndex(v => new { v.NextAttemptAt, v.IngestedAt })
            .HasDatabaseName("idx_videos_q_transcript")
            .HasFilter("\"Status\" = 'Discovered' AND \"Parked\" = 0");
        b.Entity<Video>()
            .HasIndex(v => new { v.NextAttemptAt, v.TranscribedAt })
            .HasDatabaseName("idx_videos_q_summary")
            .HasFilter("\"Status\" = 'Transcribed' AND \"Parked\" = 0");

        // Libraries / Projects
        b.Entity<Library>().HasIndex(l => l.Name).IsUnique();
        b.Entity<Library>().HasIndex(l => l.Slug).IsUnique();
        b.Entity<Project>().HasIndex(p => p.Name).IsUnique();
        b.Entity<Project>().HasIndex(p => p.Slug).IsUnique();

        // Join entity
        b.Entity<VideoProject>().HasKey(vp => new { vp.VideoId, vp.ProjectId });

        // Wiki job queue
        b.Entity<ProjectWikiJob>()
            .HasIndex(j => new { j.NextAttemptAt, j.EnqueuedAt })
            .HasDatabaseName("idx_wiki_jobs_q")
            .HasFilter("\"Status\" = 'Queued'");
        b.Entity<ProjectWikiJob>()
            .HasIndex(j => new { j.ProjectId, j.Status })
            .HasDatabaseName("idx_wiki_jobs_project");

        // Activity log
        b.Entity<ActivityEntry>().HasIndex(a => a.CreatedAt)
            .HasDatabaseName("idx_activity_recent")
            .IsDescending();
    }
}
```

**FTS5 in a hand-edited migration.** EF generates an empty migration via `dotnet ef migrations add AddVideoFts`; the body is filled in directly. *Status as of writing:* the `AddVideoFts` migration is staged but its `Up`/`Down` bodies are intentionally empty until Phase 6 (library-wide search) lands and the Videos table has real content to index — see the placeholder comment in `Db/Migrations/<timestamp>_AddVideoFts.cs`. The SQL below is what the body becomes when Phase 6 starts:

```csharp
// Migrations/<timestamp>_AddVideoFts.cs
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("""
        CREATE VIRTUAL TABLE video_fts USING fts5(
          title, summary_md, transcript_text,
          content='Videos', content_rowid='Id',
          tokenize='porter unicode61'
        );

        -- Triggers fire only on UPDATE OF (Title, SummaryMd, TranscriptText);
        -- Status/RetryCount churn does not re-index.
        CREATE TRIGGER videos_ai AFTER INSERT ON Videos BEGIN
          INSERT INTO video_fts(rowid, title, summary_md, transcript_text)
          VALUES (new.Id, new.Title, new.SummaryMd, new.TranscriptText);
        END;

        CREATE TRIGGER videos_au AFTER UPDATE OF Title, SummaryMd, TranscriptText
          ON Videos BEGIN
          INSERT INTO video_fts(video_fts, rowid, title, summary_md, transcript_text)
            VALUES('delete', old.Id, old.Title, old.SummaryMd, old.TranscriptText);
          INSERT INTO video_fts(rowid, title, summary_md, transcript_text)
            VALUES (new.Id, new.Title, new.SummaryMd, new.TranscriptText);
        END;

        CREATE TRIGGER videos_ad AFTER DELETE ON Videos BEGIN
          INSERT INTO video_fts(video_fts, rowid, title, summary_md, transcript_text)
            VALUES('delete', old.Id, old.Title, old.SummaryMd, old.TranscriptText);
        END;
        """);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("""
        DROP TRIGGER IF EXISTS videos_ad;
        DROP TRIGGER IF EXISTS videos_au;
        DROP TRIGGER IF EXISTS videos_ai;
        DROP TABLE IF EXISTS video_fts;
        """);
}
```

FTS5 indexes `Title + SummaryMd + TranscriptText` so spoken proper nouns ("Llama 3", "Postgres") stay findable verbatim. No analysis column — analysis lives in per-Project wiki Markdown on disk and is searched separately when a Project is the active scope.

Five design notes worth recording. **First:** a video has at most one `LibraryId` (single-select, since Library hides from the main feed) but can sit in many projects via `VideoProject` (Projects keep the video visible). Each Project sees the video through its own lens — a video added to two Projects produces two independent wiki update jobs, and the resulting wiki pages don't cross-reference between Projects. That's intentional. **Second:** the timestamped transcript is stored on `Video.TranscriptText` for MVP. It is the source of truth for citations in wiki pages — there is no `AnalysisMd` property; per-video analysis only exists as Markdown inside each Project's wiki. **Third:** the cheap fields (`Title`, `PublishedAt`, `ThumbnailUrl`) come from `playlistItems.list` and let us render a card immediately; `DurationSeconds`, `ViewCount`, `LikeCount`, and `Description` come from `videos.list` in the same round; only the transcript and the LLM short summary require waiting for the workers. **Fourth:** `Status` drives the two pipeline queues (Discovered → Transcribed → Ready). The wiki worker is driven by a *separate queue* (`ProjectWikiJobs`), not by `Video.Status`, because wiki work is not part of the per-video pipeline — it's per-(project, video) and happens later, on user action. **Fifth:** search uses one virtual table. Per-Project wiki search is a separate surface (grep over Markdown files, or a small per-project FTS5 table v2 if needed).

**Wiki on disk.** Layout under `<data-dir>/wiki/<project-slug>/`:

```
index.md       # catalog: one line per page, with one-line hooks. Always loaded
               # first by the wiki worker so the LLM sees the whole shape.
SCHEMA.md      # editorial rules: page structure, naming, citation format,
               # conflict resolution. Loaded into the wiki worker's context
               # on every ingest pass.
pages/         # one Markdown file per topic. The LLM creates, updates, and
               # occasionally consolidates these. Citations link back to videos
               # in the form [Video Title @ 14:32](/videos/<id>?t=872).
```

The user can read or hand-edit any of these files; the next ingest pass respects manual edits as long as the file still parses against `SCHEMA.md`.

---

## 6. MVP scope and phasing

Eight phases (Phase 0 through Phase 7), roughly 4–5 weeks of focused single-developer work. Each phase ends with something demoable.

### Phase 0 — Skeleton (1–2 days)
Repo init: `SkipWatch.slnx` with the Blazor Server host project (`SkipWatch`) and the `SkipWatch.Core` library. `Directory.Build.props` enabling nullable refs, implicit usings, and `TreatWarningsAsErrors`. Blazor Server host with a `MainLayout.razor`, a placeholder `Home.razor`, and the Options pattern wired up to read `appsettings.json` + user-secrets (dev only) + environment variables into typed settings records (`YouTubeApiSettings`, `ApifySettings`). Bootstrap 5 referenced from the jsDelivr CDN with SRI hashes (CSS + Bootstrap Icons + JS bundle, exact versions and hashes lifted from `TargetBrowse/Components/App.razor`). The harvested `wwwroot/app.css`, `wwwroot/css/components/`, and `Components/Layout/{MainLayout,NavMenu}.razor` (+ `.razor.css`) lifted from TargetBrowse provide the dark theme, top menu, left sidebar, and message-center shell. No CSS build step, no vendored Bootstrap. SQLite + EF Core wired into `Program.cs` (`AddDbContext<SkipWatchDbContext>` with the SQLite provider); `context.Database.Migrate()` runs at startup against `~/.skipwatch/skipwatch.db`. `dotnet-ef` installed as a local tool (`dotnet tool install dotnet-ef`) so `dotnet ef migrations add <Name>` works without a global install. Initial migration generates the regular tables; `AddVideoFts` is added immediately after as an empty migration whose body stays empty until Phase 6, when it is filled in by hand with the FTS5 virtual table + triggers. Default data dir `~/.skipwatch/`; create `~/.skipwatch/wiki/` on first run.

**Outstanding from this phase** (deferred, not yet done): `SkipWatch.Tests` project (xUnit + FluentAssertions + bUnit), `.editorconfig` + `dotnet format` in CI, the `/health` minimal-API endpoint. Two debug endpoints (`/debug/yt/channel/{handleOrId}`, `/debug/transcript/{videoId}`) currently exist as Phase-1/Phase-2 scaffolding and are removed once their respective workers land.

### Phase 1 — Discovery round (3–5 days)
**Adding a channel.** Resolver calls `channels.list` with `forHandle` (or `forUsername`/`id` depending on the input format) and stores the canonical channel ID, title, handle, and `contentDetails.relatedPlaylists.uploads` (the uploads-playlist ID — the playlist that contains every upload from the channel). One quota unit, one time. `enabled=1` and `last_check_at=NULL`.

**The round.** A single hosted service, `CollectionRoundService`, fires on the schedule `SKIPWATCH_ROUND_CRON` (default `*/30 * * * *`, parsed by NCrontab; the simple `*/30` form short-circuits to a plain `PeriodicTimer(TimeSpan.FromMinutes(30))`). Each round picks up to `CHANNELS_PER_ROUND=5` channels with this query:

```sql
SELECT id FROM channels
WHERE enabled = 1
  AND (last_check_at IS NULL OR last_check_at < datetime('now', '-24 hours'))
ORDER BY last_check_at IS NULL DESC, last_check_at ASC
LIMIT 5;
```

Never-checked channels first, then oldest-checked. The 24-hour gate guarantees a channel is visited at most once per day no matter how empty the round is. `last_check_at` is updated whether the visit succeeds or fails so a broken channel can't block rotation.

**Per channel.** Two Data API calls and a duration filter — that is the whole round:

1. **`playlistItems.list`** against `uploads_playlist_id` with `maxResults=50` and `part=snippet,contentDetails`. Cap results at `INITIAL_VIDEO_CAP=20` for cold-start (no rows for this channel yet) or `ROLLING_VIDEO_CAP=10` otherwise. Stop paging the moment we hit a video already in the DB.
2. **`videos.list`** (1 unit per page of 50) on the new IDs with `part=contentDetails,statistics` for `duration` (ISO 8601), `viewCount`, `likeCount`, `commentCount`.
3. **Duration gate.** `duration ≤ MIN_VIDEO_DURATION_SECONDS` (default 180s) → insert with `status='skipped_short'`. `duration > MAX_VIDEO_DURATION_MINUTES` (default 60) or unknown → insert with `status='skipped_too_long'`. Otherwise → insert with `status='discovered'`. Inserting a `discovered` row is the entire enqueue mechanism — the transcript worker is already polling for it.

The round itself never calls Apify and never invokes the LLM, so it always finishes in seconds and a slow downstream worker can never push out the next round.

**Quota math.** 5 channels × 2 calls × 48 rounds/day = **480 units/day** — under 5% of the 10k/day default quota. Adding more channels does not raise the burn since the round size is fixed.

**Constants live in config.** `SKIPWATCH_ROUND_CRON`, `INITIAL_VIDEO_CAP`, `ROLLING_VIDEO_CAP`, `MIN_VIDEO_DURATION_SECONDS`, `MAX_VIDEO_DURATION_MINUTES`, `CHANNELS_PER_ROUND`, `MAX_RETRY_ATTEMPTS=3` all in the standard configuration chain (`appsettings.json` → user-secrets → environment variables).

### Phase 1b — Topic discovery (2–3 days)

A second discovery surface, harvested in concept from TargetBrowse but **without the rating system, scoring algorithm, or "Suggestions" entity**. Topics produce the same `status='discovered'` rows that channel discovery does — the transcript and summary workers are oblivious to which source produced a video.

**Adding a topic.** UI form captures `Name` and a raw YouTube search `Query` (advanced operators allowed). No vetting at add-time — saved straight to `topics` with `enabled=1`, `last_check_at=NULL`. Optional `LookbackDays` slider (default 7).

**The topic round.** `CollectionRoundService` is extended (or a sibling `TopicRoundService` is added — same `IHostedService` pattern, same `*/30` cadence) to pick up to `TOPICS_PER_ROUND=5` enabled topics not visited in the last 24 hours. Per topic:

1. **`search.list`** with `q=<topic.Query>`, `type=video`, `order=date`, `publishedAfter=now() - LookbackDays`, `maxResults=50`. **100 quota units per call** — substantially more expensive than `playlistItems.list`, which is why the round is small and gated. Cap at `TOPIC_RESULTS_CAP=20` per topic.
2. For any returned video IDs not already in `videos`, run the same `videos.list` enrichment (`part=contentDetails,statistics`) and the same duration gate as channel discovery. New rows are inserted with `status='discovered'`. For video IDs already in the DB (because a followed channel found them, or another topic did), insert a `topic_videos` row only — do not duplicate the video.
3. Always insert the `topic_videos` row, even when the video pre-exists, so source provenance is tracked. Video cards in the UI render small source badges: `Channel`, `Topic: <name>`, or both when a video has multiple sources.
4. Update `topic.last_check_at` whether the visit succeeds or fails.

**Quota math.** 5 topics × 100 units × 48 rounds/day = **24,000 units/day** for topics alone — well over the 10k/day default quota. Two mitigations baked in:
- `TOPICS_PER_ROUND` defaults to **2** (not 5) so the daily topic spend is ~9,600 units, leaving headroom for channel discovery (~480) and ad-hoc operations.
- The **24-hour gate** is enforced strictly: a topic is visited at most once per day. Users who add many topics get coverage spread across days, not quota exhaustion in hours.
- `YouTubeQuotaManager` (harvested singleton from TargetBrowse) tracks daily burn and refuses calls once a configurable ceiling (`YOUTUBE_DAILY_QUOTA_CEILING`, default 9000) is hit, deferring further discovery until UTC rollover.

**No scoring, no ratings.** Topic-discovered videos enter the same triage feed as channel-discovered ones — Library / Project / Pass. There is no "topic relevance score", no "channel rating", no weighted ranking. The user's triage actions *are* the ranking signal.

**Filters and search (Phase 4 follow-up).** The dashboard filter set gains a "By topic" facet alongside "By channel"; FTS5 search results show all source badges. No new search infrastructure.

**Constants live in config.** `TOPICS_PER_ROUND`, `TOPIC_RESULTS_CAP`, `YOUTUBE_DAILY_QUOTA_CEILING` join the existing constants in the standard configuration chain.

### Phase 2 — Transcript worker (Q1: Apify) (3–4 days)
A `BackgroundService` running a continuous async loop (`while (!stoppingToken.IsCancellationRequested)`). On each tick:

```sql
SELECT * FROM videos
WHERE status = 'discovered' AND parked = 0
ORDER BY ingested_at ASC
LIMIT 1;
```

Concurrency for this worker is `TRANSCRIPT_WORKER_CONCURRENCY` (default 1 — keeps Apify spend serial and predictable; user can raise it in settings for cold-start of a large follow list). Per video, the worker:

1. Calls the [`streamers/youtube-scraper`](https://apify.com/streamers/youtube-scraper) actor through `ApifyClient` (a typed `HttpClient` registered via `IHttpClientFactory`, hitting `run-sync-get-dataset-items` so a single request returns the run's items inline). Single-video `startUrl`, `includeSubtitles=true` (plain-text format, **with cue offsets preserved**). The actor handles manual captions, auto-captions, and a Whisper fallback for videos with no captions at all.
2. Deserializes the response with `System.Text.Json` and flattens the first usable entry from `subtitles[]` into `transcript_text` as timestamped lines (`[mm:ss] line text`), sets `transcript_lang` and `has_transcript=1`. Timestamped form is required because wiki pages cite specific moments. If no usable subtitle came back, transitions the row to `status='no_transcript'` — the dashboard renders it with a "No transcript" badge and the three triage buttons still work, but the LLM is never invoked. (YouTube descriptions are too thin/promotional to make a useful summary on their own.)
3. Overwrites the cheap-field columns with Apify's richer values (`description`, `duration_s`, `view_count`, `like_count`, `comments_count`) since they're fresher than the Data API's.
4. Transitions the row to `status='transcribed'`, sets `transcribed_at=now()`, **resets `retry_count=0`**.
5. On exception: `retry_count++`, `last_error=str(exc)`. If `retry_count >= MAX_RETRY_ATTEMPTS`, sets `parked=1` and `parked_at=now()`. Otherwise the row stays `discovered` and the worker will pick it up again on its next eligible tick. Backoff is exponential per row (`min(60s × 2^retry_count, 1h)`) gated by the `next_attempt_at` column.

**Cost.** Apify is billed per result (~$0.005/video). Given the round caps, throughput is bounded by discovery, not the worker — expect $5–$15/month for a typical 100-channel follow list. The settings page surfaces a running spend estimate.

### Phase 3 — Summary worker (Q2: short summaries only) (2–3 days)
A second `BackgroundService`, identical shape to Phase 2's worker but pulling from the other queue:

```sql
SELECT * FROM videos
WHERE status = 'transcribed' AND parked = 0
ORDER BY transcribed_at ASC
LIMIT 1;
```

Concurrency is fixed at **1** (`SUMMARY_WORKER_CONCURRENCY=1`) — local LLM inference is RAM/GPU-bound, parallel inferences just thrash. Per video:

1. **Map-reduce summarization** over `transcript_text` with `Qwen3-Coder-30B-A3B-Instruct-Q4_K_M` (configurable via `SUMMARY_MODEL`). Split at sentence boundaries into ~300-token chunks with ~30-token overlap, summarize each chunk, then a final reduce prompt produces **only the 1–2 paragraph card summary** — no long-form analysis. The reduce prompt enforces the "lead with the subject matter, no fluff, decision-support framing" rule.
2. **Structured output** via Ollama's `format` parameter (JSON Schema mode) called through `OllamaSharp`, deserialized with `System.Text.Json` into a `SummaryResponse(string SummaryMd, DecisionSignal Signal)` record. The worker reliably gets `{summary_md, decision_signal: "watch"|"skim"|"skip"}`. Anything else throws and trips the retry path.
3. Writes `summary_md`, `decision_signal`, `summarized_at=now()`. Transitions to `status='ready'`. **Resets `retry_count=0`**. The card is now visible on the dashboard.
4. On exception: same retry/park semantics as Phase 2.

**Why no analysis here.** Long-form analysis is only valuable for videos the user has decided to research further (added to a Project). Producing it for every video — including the 70–80% that get Passed or merely Library'd — is wasted LLM time. Analysis is deferred to the wiki worker, scoped to the Project the video joins, where it can also benefit from the surrounding context of the Project's existing wiki pages.

Because the queues are independent, an Apify outage stops Q1 dead but Q2 keeps draining whatever transcripts already arrived; an LLM crash stops Q2 dead but Q1 keeps stockpiling transcripts cheaply.

### Phase 4 — Triage UI (4–6 days)
`Dashboard.razor` is a single scrollable feed of `VideoCard` components (thumbnail, title, channel, summary, three action buttons). Cards subscribe to a singleton `JobEventBus` so phase advances flip individual cards without re-querying the database. The three actions are the whole UX:
- **Library ▾** opens a popover (`LibraryPicker.razor`) with the user's libraries plus "+ New library". Selecting one calls the injected `IVideoCommands.SetLibraryAsync(...)`, status becomes `'libraried'`, and the card disappears from the main feed.
- **Project ▾** opens a popover with projects plus "+ New project". Selecting one inserts into `video_projects` **and enqueues a `project_wiki_jobs` row with `action='add'`**, status becomes `'projected'`, and the card stays visible.
- **Pass** sets `status='passed'`. Card disappears. A "Show passed" toggle in settings brings them back.

**Removing from a Project** (from the Project page) deletes the `video_projects` row and enqueues a `project_wiki_jobs` row with `action='remove'`, so the wiki gets cleaned up eagerly.

**What appears on the dashboard.** By default: `ready` rows (summary done, awaiting triage) and `projected` rows (kept visible by definition). `skipped_too_long` rows render with a "Too long — no summary" badge and `no_transcript` rows render with a "No transcript" badge; in both cases the three triage buttons still work, so the user can file or pass without a summary. Rows with `parked=1` render with a "Retry" affordance regardless of which phase they're parked in. `discovered` and `transcribed` rows (still in pipeline) render as skeleton placeholders with a phase indicator. `skipped_short`, `libraried`, and `passed` are hidden by default.

**Card vs details surfaces.** The card shows the short `summary_md` rendered with Markdig. Clicking the card navigates to `/videos/{id}` (`VideoDetails.razor`) with the raw `description` from YouTube, channel info, the list of Projects this video belongs to (with links to each Project's wiki), and a link out to the video. The transcript is not rendered by default but is searchable.

Filters: by channel, by library/project, by date range, by status. Sort: newest first by default.

### Phase 5 — Wiki worker and Project view (5–7 days)
The wiki worker is the heart of the new design. A third `BackgroundService` draining `project_wiki_jobs`:

```sql
SELECT * FROM project_wiki_jobs
WHERE status = 'queued'
  AND (next_attempt_at IS NULL OR next_attempt_at <= datetime('now'))
ORDER BY enqueued_at ASC
LIMIT 1;
```

Concurrency fixed at **1** (`WIKI_WORKER_CONCURRENCY=1`) — same LLM as the summary worker, same single-process constraint. Per job:

1. **Lock the project.** Set `projects.wiki_status='updating'`. Set the job's `status='running'`, `started_at=now()`.
2. **Coalesce.** Within a short debounce window (default 10 s after the *first* queued job for a project lands), pull all queued jobs for that same project and process them as one ingest pass. This keeps "the user added five videos to a Project in 30 seconds" from triggering five back-to-back LLM passes. Eager but not stupid.
3. **Load context.** Read `<data-dir>/wiki/<project-slug>/index.md`, `SCHEMA.md`, and the Project's row (name, description). For each `add` job, also load the video's `summary_md`, `transcript_text`, `title`, channel, and `published_at`. For each `remove` job, just record the video id.
4. **Plan.** Single LLM call: given `index.md` + `SCHEMA.md` + the new/removed videos, decide which wiki pages need creating, updating, or deleting. Output is a structured plan: `[{action: "create"|"update"|"delete", page: "filename.md", reason: "..."}]`.
5. **Execute.** For each page in the plan, a second LLM call generates the new Markdown content using the relevant transcript excerpts + the existing page content (if updating). Citations follow the rule from `SCHEMA.md`: `[Video Title @ 14:32](/videos/<id>?t=872)`. Writes are atomic via `File.WriteAllTextAsync(tempPath, ...)` followed by `File.Move(tempPath, finalPath, overwrite: true)`.
6. **Update `index.md`.** A final pass rewrites the catalog to reflect any new/removed pages. Always one-line entries, kept under the context-window threshold.
7. **Mark done.** Set all coalesced jobs to `status='done'`, `completed_at=now()`. Set `projects.wiki_status='idle'`, `wiki_updated_at=now()`.
8. On exception: same retry/park semantics. The Project's `wiki_status` flips to `'error'` and the Project page surfaces a banner with the error and a "Retry" button.

**Project page UI.** Route `/projects/{slug}` → `ProjectPage.razor`, which composes:
- `ProjectHeader.razor` with name, description, video count, and a status indicator subscribed to `JobEventBus` so "Wiki updated 2 minutes ago" / "Wiki updating…" / "Wiki out of sync — retry" flips live as the wiki worker runs.
- Tabs: **Videos** (`ProjectVideos.razor` — list of videos in the project, each with a remove button) and **Wiki** (`ProjectWiki.razor` — renders `index.md` with links to each page, and a page viewer using Markdig).
- The Wiki tab is read-only in MVP — editing requires opening the Markdown file in an external editor (which is fine, the design supports it). v2 may add an in-app editor.

**Token economics.** Per the Karpathy pattern, expect ~40–60k tokens per ingest pass: the index, SCHEMA, the affected pages (typically 2–5), and the new transcript. Coalescing helps amortize the index-load cost across multiple videos added together. On Qwen3-Coder-30B Q4_K_M with a GPU, a pass takes ~30 s–2 min. On CPU, 5–15 min — slow, but always async, never blocking.

### Phase 6 — Library-wide search (3–4 days)
A `SearchBox.razor` component pinned to the top of the dashboard, backed by **FTS5** over `title + summary_md + transcript_text`. BM25 ranking (FTS5's default), `snippet()` for highlighted excerpts. Catches exact terms, proper nouns, and phrase queries — e.g. `"Llama 3"`, `Postgres`, a remembered turn-of-phrase from something the host said.

**Result rendering.** Hits show the card thumbnail/title/channel plus a snippet excerpt. Results group by status so the user can see at a glance whether a hit is from their library, a project, or the unfiltered backlog. Filters from Phase 4 (channel, library/project, date) apply to search results too.

**Per-Project wiki search.** When the user is on a Project page, a second search box (or scoped tab) does plain substring/grep across the Markdown files in that Project's wiki folder. Cheap, deterministic, no index needed at MVP scale. If wikis grow large enough to warrant indexing, a per-project FTS5 table is a v2 addition.

**Why no semantic / hybrid search.** The wiki *is* the semantic surface. Inside a Project, the LLM-compiled pages already cluster meaning across videos with explicit cross-refs and a curated index — that beats reconstructing semantic context from chunk embeddings at query time. Outside a Project, FTS5 is the right tool: triage queries are usually proper-noun or phrase queries, not conceptual.

### Phase 7 — Polish and packaging (2–3 days)
Settings page exposing the config knobs introduced across phases: `SUMMARY_MODEL`, `SKIPWATCH_ROUND_CRON`, `CHANNELS_PER_ROUND`, the two video caps, the duration thresholds, `TRANSCRIPT_WORKER_CONCURRENCY`, `WIKI_WORKER_CONCURRENCY`, the wiki coalesce debounce, `MAX_RETRY_ATTEMPTS`, and a "show passed videos" toggle (all written to a writable `appsettings.Local.json` overlay layered on top of the standard configuration chain). Plus credential fields for the YouTube Data API key and Apify token. Credentials are written to user-secrets when running from source (`dotnet user-secrets set ...`), or to standard environment variables on the user's machine for the shipped binary (the settings-page form sets them via `setx` on Windows / writing to `~/.profile` on Unix, with a one-time relaunch prompt). Logs pane tails recent rows from the `activity` table. `dotnet publish -c Release --self-contained -p:PublishSingleFile=true` produces `SkipWatch.exe` (`win-x64`), `SkipWatch` (`osx-arm64`, `linux-x64`). The launcher script (`scripts/launch.ps1` on Windows, `scripts/launch.sh` on macOS/Linux) checks the LLM runtime is reachable, pulls the configured summary model if not present, ensures `~/.skipwatch/wiki/` exists, starts the binary (which applies EF Core migrations on its own at startup), and opens the browser at `http://localhost:7860`.

---

## 7. v2 backlog (deferred from MVP)

Listed in roughly the order they would unlock the most value:

1. **Project-scoped chat with cited answers.** A chat box on a Project page that answers questions by reading the Project's wiki pages (selected via `index.md`) plus the underlying transcripts when needed for verbatim quotes. Inline citations link back to wiki pages and video timestamps. Builds directly on the MVP wiki surface.
2. **Project guides and reports.** A "Generate report" button on a Project that compiles a structured Markdown deliverable from the existing wiki pages. Effectively a wiki-of-wiki pass.
3. **In-app wiki editing.** Markdown editor for wiki pages, with live preview. Edits are respected by the next ingest pass as long as they parse against `SCHEMA.md`.
4. **Wiki lint pass.** A "Rebuild wiki" button on a Project that re-runs ingest from scratch over all current videos. Useful after the user upgrades the LLM or changes `SCHEMA.md`.
5. **Apify Whisper fallback** for videos with no captions (~15% of the long tail). Wired behind a "use Apify for missing transcripts" setting since it costs money.
6. **Pluggable LLM providers.** The `ISummarizer` interface gets additional implementations for Anthropic Claude (`Anthropic.SDK`) and OpenAI (`OpenAI` NuGet), configurable per-task via DI keyed services (e.g. local Ollama for short summaries, cloud Claude for wiki ingest where quality matters more).
7. **"More like this"** on a video card. Without embeddings this is harder; v2 candidates are LLM-classified topic tags or wiki-cross-reference lookup.
8. **Channel auto-tagging.** Suggest a default Library when adding a new channel, based on a quick LLM look at the channel's recent video titles.
9. **Per-Project FTS5 over wiki Markdown.** Only needed if a Project's wiki ever exceeds the size where grep is slow (unlikely in single-user MVP scale).

---

## 8. Risks and open questions

The biggest risk is **Apify dependency**: the transcript path is a paid third-party service, not a local library. Three concrete failure modes to plan for: (a) the user runs out of credits or hits a spending cap; (b) Apify's actor itself has a bad day and times out; (c) the user is offline. All three flow through the same path in Q1 — `retry_count++`, exponential backoff via `next_attempt_at`, `parked=1` once `retry_count >= MAX_RETRY_ATTEMPTS`. A circuit breaker pauses Q1 entirely after N consecutive failures across all rows (default 5) and surfaces a banner in the UI; the summary worker keeps draining Q2 with whatever transcripts already arrived. A "Retry" button on each parked card resets `retry_count=0` and `parked=0`. The interface around Apify is kept narrow (one `ITranscriptSource.FetchAsync(string videoId, CancellationToken ct) -> Task<Transcript>` method) so a v2 can swap in a different provider or a local `yt-dlp + Whisper.net` pipeline without touching the rest of the app.

The second risk used to be **YouTube Data API quota** but the round design defuses it: 5 channels × 2 calls × 48 rounds = 480 units/day regardless of registered channel count. The trade-off is **freshness on a large follow list**: at 5 channels/round and a 24h gate, every channel is visited at most once per day, so a video posted at 9am on a channel that was just visited at 8:55am will sit unseen until tomorrow morning's window. That is acceptable for a triage product (and even philosophically aligned with the "calm" goal in the description), but worth confirming. If a user wants more aggressive freshness for a small follow list, raising `CHANNELS_PER_ROUND` is the lever.

The third risk is **wiki-ingest LLM cost and latency**. Each add-to-Project triggers an LLM pass over the Project's index + a few pages + the new transcript — roughly 40–60k tokens. Coalescing within a debounce window helps when the user adds several videos at once. The hard ceiling is the per-Project wiki size: past ~150–200 pages, `index.md` no longer fits in a single context window. Mitigations: (a) Projects are user-curated and naturally bounded; (b) the wiki worker can fall back to summarizing `index.md` itself once it exceeds a token budget; (c) v2 can split very large Projects into sub-Projects. None of this matters until a Project crosses ~100 pages, which is unlikely in normal use.

The fourth risk is **wiki-page drift over many edits**. Repeated LLM rewrites of the same page can degrade quality — slowly losing structure, accreting redundant bullets, or contradicting earlier claims. Mitigations: (a) `SCHEMA.md` is loaded into every ingest pass, anchoring structure; (b) the v2 "Rebuild wiki" lint pass re-runs the whole ingest from scratch when needed; (c) wiki files are plain Markdown, so the user can always open one and clean it up by hand. Worth eyeballing wiki pages periodically on an active Project to confirm structure is holding.

The fifth risk is **local LLM performance on CPU-only machines**. Qwen3-Coder-30B-A3B is a mixture-of-experts model with only ~3B active parameters, which keeps inference roughly in the speed class of a dense 3B — but the Q4_K_M GGUF is still ~17 GB and needs to fit in RAM. On a modern laptop with 32 GB RAM and no GPU, expect 30–60 seconds per chunk for short summaries and 5–15 minutes for a wiki ingest pass; on a GPU with 24 GB VRAM it is conversational. Because both `SUMMARY_WORKER_CONCURRENCY=1` and `WIKI_WORKER_CONCURRENCY=1`, slow inference doesn't fan out into resource thrash — it just lengthens queues. `SUMMARY_MODEL` is exposed in settings so users on tight hardware can drop to a smaller Qwen3 variant; the wiki worker can use the same setting or a separate `WIKI_MODEL` (v2 — for users who want a stronger model just for the expensive synthesis step).

Decisions locked in before implementation:
- **Round cadence and size.** `SKIPWATCH_ROUND_CRON = */30 * * * *`, `CHANNELS_PER_ROUND = 5`, 24h gate. Sized for the user's expected 75–100-channel follow list — 5 channels × 48 rounds/day = 240 visit-slots, comfortably above 100, so every channel is reached well within the 24h window even with retries.
- **Duration thresholds.** `MIN_VIDEO_DURATION_SECONDS = 180` (drops Shorts), `MAX_VIDEO_DURATION_MINUTES = 60`. The 60-min cap is intentional: longer videos are typically fluff-heavy and not worth the Apify spend.
- **Transcript worker concurrency.** `TRANSCRIPT_WORKER_CONCURRENCY = 1` by default, exposed in the settings page. Serial Apify calls make spend predictable; user can raise it for cold-start of a large new follow list.
- **Per-video LLM output.** Short summary only at the per-video stage. Long-form analysis is per-Project, produced by the wiki worker on add-to-Project.
- **Wiki update policy.** Eager, with a short debounce coalesce window (default 10 s). Rationale: Projects are actively-used research surfaces; the user wants to see the result of adding/removing a video promptly, not on a nightly batch.
- **Wiki concurrency.** `WIKI_WORKER_CONCURRENCY = 1`. Same LLM as the summary worker, same single-process constraint.
- **Wiki storage.** Plain Markdown on disk under `<data-dir>/wiki/<project-slug>/`. The DB tracks job state and project metadata only.
- **No-transcript fallback.** When Apify returns no usable subtitle, the row transitions to `status='no_transcript'` and shows on the dashboard with a "No transcript" badge — three triage buttons still work, no LLM call. Description-as-fallback is rejected: YouTube descriptions are too thin to make a useful summary.
- **Database location.** `~/.skipwatch/skipwatch.db` on all platforms, with `~/.skipwatch/wiki/` as a sibling. Survives reinstalls; consistent location for credentials, config, DB, and wiki.
- **Credential storage.** YouTube Data API key and Apify token via the .NET configuration chain: user-secrets in development (`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`), standard environment variables on shipped end-user machines. Tunable knobs (cron, caps, durations, concurrency, model names) live in the same configuration chain — `appsettings.json` ships the defaults, the Settings page writes overrides to a co-located `appsettings.Local.json` overlay. No separate TOML/YAML layer.

---

## 9. Repo layout

Vertical slices for user-facing features (Channels, Topics, and the future Dashboard / Projects / Settings) live under `SkipWatch/Features/<Feature>/{Components,Services,Models,Utilities}` in the App project. Cross-cutting infrastructure — entities, DbContext + migrations, the YouTube + Apify integrations, workers — lives in `SkipWatch.Core`. UI-adjacent shared services (theme, message center) sit at `SkipWatch/Services/`. Items in *italics* are not yet built and will land in their respective phases.

```
SkipWatch/
├── docs/prd.md                                ← this document (lives at workspace root)
├── SkipWatch/                                  ← repo root
│   ├── SkipWatch.slnx
│   ├── Directory.Build.props                   ← nullable, implicit usings, warnings-as-errors
│   ├── dotnet-tools.json                       ← local dotnet-ef
│   ├── *.editorconfig*                         ← outstanding (Phase 0)
│   ├── *scripts/launch.ps1, launch.sh*         ← outstanding (Phase 7)
│   ├── SkipWatch/                              ← Blazor Server host (project name: SkipWatch)
│   │   ├── SkipWatch.csproj                    ← references SkipWatch.Core
│   │   ├── Program.cs                          ← host + DI + workers + Razor components
│   │   ├── appsettings.json                    ← YouTube:*, Apify:* sections
│   │   ├── Components/
│   │   │   ├── Layout/{MainLayout,NavMenu}.razor (+ .razor.css)
│   │   │   ├── Pages/                          ← *Dashboard, VideoDetails, ProjectPage, Settings*
│   │   │   └── Shared/                         ← MessageSidebar.razor (+ .razor.cs);
│   │   │                                          *VideoCard, LibraryPicker, ProjectPicker,
│   │   │                                          ProjectHeader, ProjectVideos, ProjectWiki,
│   │   │                                          SearchBox*
│   │   ├── Features/                           ← vertical slices
│   │   │   ├── Channels/
│   │   │   │   ├── Components/Channels.razor (+ .razor.cs)
│   │   │   │   ├── Services/{IChannelService,ChannelService}.cs
│   │   │   │   ├── Models/{ChannelDto,AddChannelResult}.cs
│   │   │   │   └── Utilities/YouTubeChannelInputParser.cs
│   │   │   └── Topics/
│   │   │       ├── Components/Topics.razor (+ .razor.cs)
│   │   │       ├── Services/{ITopicService,TopicService}.cs
│   │   │       └── Models/{TopicDto,AddTopicResult}.cs
│   │   ├── Services/                           ← UI-adjacent shared services
│   │   │   ├── ThemeService.cs + Interfaces/IThemeService.cs
│   │   │   ├── MessageCenterService.cs + Interfaces/IMessageCenterService.cs
│   │   │   └── Models/MessageState.cs
│   │   ├── *Endpoints/HealthEndpoints.cs*      ← outstanding (Phase 0)
│   │   └── wwwroot/
│   │       ├── css/components/                 ← per-component CSS (harvested)
│   │       ├── app.css                         ← global theme + dark mode (harvested)
│   │       └── favicon.png                     ← Bootstrap loaded from jsDelivr CDN, not vendored
│   └── SkipWatch.Core/
│       ├── SkipWatch.Core.csproj
│       ├── Entities/                           ← Channel, Topic, TopicVideo, Video,
│       │                                          Library, Project, VideoProject,
│       │                                          ProjectWikiJob, ActivityEntry + enums
│       ├── Db/
│       │   ├── SkipWatchDbContext.cs           ← DbSets + OnModelCreating
│       │   ├── DesignTimeDbContextFactory.cs   ← lets `dotnet ef` find the context
│       │   └── Migrations/                     ← Initial + AddVideoFts (body deferred to Phase 6)
│       └── Services/
│           ├── Interfaces/                     ← IYouTubeApiService, IYouTubeQuotaManager,
│           │                                      ITranscriptSource
│           ├── YouTube/
│           │   ├── YouTubeApiService.cs        ← Google.Apis.YouTube.v3 — channel resolver +
│           │   │                                 uploads-playlist poller
│           │   ├── YouTubeQuotaManager.cs      ← persisted daily-burn tracker + reservations
│           │   ├── DurationParser.cs           ← ISO 8601 → seconds
│           │   └── Models/YouTubeApiSettings.cs
│           ├── Transcripts/
│           │   ├── ApifyTranscriptSource.cs    ← typed HttpClient over streamers/youtube-scraper
│           │   ├── ApifySettings.cs
│           │   └── Transcript.cs               ← result record returned by ITranscriptSource
│           ├── Utilities/                      ← FormatHelper, TextFormatter, ThumbnailFormatter,
│           │                                      SrtConverter (harvested helpers)
│           ├── *ISummarizer.cs / OllamaSummarizer.cs*  ← outstanding (Phase 3)
│           ├── *Wiki.cs*                       ← outstanding (Phase 5)
│           ├── *Search.cs*                     ← outstanding (Phase 6)
│           ├── *IVideoQueries.cs / IVideoCommands.cs*  ← outstanding (Phase 4)
│           ├── *JobEventBus.cs*                ← outstanding (Phase 4)
│           └── *Workers/*                      ← outstanding
│               ├── *CollectionRoundService.cs* ← IHostedService + PeriodicTimer (Phase 1)
│               ├── *TranscriptWorker.cs*       ← Q1 discovered → transcribed (Phase 2)
│               ├── *SummaryWorker.cs*          ← Q2 transcribed → ready (Phase 3)
│               └── *WikiWorker.cs*             ← Q3 project_wiki_jobs → Markdown (Phase 5)
└── *tests/SkipWatch.Tests/*                    ← outstanding (Phase 0): xUnit + FluentAssertions + bUnit
```

---

## 10. References

Ingestion and transcripts:
- [YouTube Data API v3 quota reference](https://developers.google.com/youtube/v3/determine_quota_cost)
- [YouTube Data API: PlaylistItems: list](https://developers.google.com/youtube/v3/docs/playlistItems/list) (1-unit cost; primary discovery endpoint)
- [Apify `streamers/youtube-scraper` actor](https://apify.com/streamers/youtube-scraper) (transcript + rich metadata, ~$0.005/video)

Summarization:
- [Qwen3-Coder model release notes](https://qwenlm.github.io/blog/qwen3-coder/)
- [GGUF quantization formats (Q4_K_M etc.)](https://huggingface.co/docs/hub/en/gguf)
- [Ollama structured outputs (JSON schema)](https://ollama.com/blog/structured-outputs)
- [Map-reduce summarization for long transcripts (yt-sum gist)](https://gist.github.com/drkarl/7169dd7234d2efc42221c17e3a91d50a)
- [LLM summarization strategies overview](https://galileo.ai/blog/llm-summarization-strategies)

Wiki / knowledge-base pattern:
- [Karpathy shares 'LLM Knowledge Base' architecture (VentureBeat)](https://venturebeat.com/data/karpathy-shares-llm-knowledge-base-architecture-that-bypasses-rag-with-an)
- [Beyond RAG: Karpathy's LLM Wiki Pattern (Level Up Coding)](https://levelup.gitconnected.com/beyond-rag-how-andrej-karpathys-llm-wiki-pattern-builds-knowledge-that-actually-compounds-31a08528665e)
- [LLM Wiki: Karpathy's 3-Layer Pattern (decodethefuture.org)](https://decodethefuture.org/en/llm-wiki-karpathy-pattern/)
- [LLM Wiki v2 — extending Karpathy's pattern (GitHub Gist)](https://gist.github.com/rohitg00/2067ab416f7bbe447c1977edaaa681e2)

Search and storage:
- [SQLite FTS5 documentation](https://www.sqlite.org/fts5.html)

Comparable projects:
- [Tube Archivist — self-hosted YouTube media server](https://github.com/tubearchivist/tubearchivist)
- [FreeTube — private YouTube desktop client](https://freetubeapp.io/)
- [ytsm — self-hosted YouTube subscription manager](https://github.com/chibicitiberiu/ytsm)
- [ytsms — alternative subscription management system](https://github.com/MDeLuise/ytsms)
- [AskTube — local YouTube summarizer + RAG with Ollama](https://github.com/jonaskahn/asktube)
- [Bricolage: build notes for a local-first AI YouTube summarizer](https://bricolage.io/build-notes-ai-powered-youtube-summarizer/)
- [Tomash Corner: Melting down Watch Later with LLMs](https://tomash.wrug.eu/blog/2024/07/15/llm-youtube-watchlater/)
