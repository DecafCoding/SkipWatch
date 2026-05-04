# SkipWatch

**Skip or watch.** SkipWatch is a single-user, locally installed app that turns the YouTube channels you follow into a calm, triaged, searchable library. Deterministic collectors fetch new videos on a schedule. An LLM summarizes each one into a short decision-support summary. You file them into Libraries, group them into Projects, or hit Pass — and your dashboard stays clean.

Designed to run on your own machine. Single user. No accounts, no cloud, no notifications.

## What it does

- **Collects** new videos from your followed YouTube channels on a schedule (YouTube Data API + Apify for transcripts).
- **Summarizes** each video into a 1–2 paragraph decision-support filter — leads with the subject matter, no fluff.
- **Triages** every video card with three first-class actions:
  - **Library ▾** — file into a consumption bucket (e.g. *Education*, *Entertainment*). Hides from the main feed.
  - **Project ▾** — group into a research collection (e.g. *AI Skills*). Stays visible in the main feed; supports synthesized guides/reports across the videos in the project.
  - **Pass** — dismiss. Hidden but recoverable.
- **Searches** the whole library with SQLite FTS5 and a smart-search chat that returns cited answers.
- **Synthesizes** rollups across multiple videos when you ask for synthesis in chat.

## What it is not

- Not a personal AI assistant.
- Not a multi-source aggregator (no RSS / podcasts / papers — different product).
- Not a Telegram bot or mobile app.
- Not a general chat product — chat is scoped to your video library.

## Tech stack

- **.NET 10** — Blazor Server (Interactive Server components)
- **EF Core + SQLite** — local data store at `~/.skipwatch/skipwatch.db`, with FTS5 for search
- **YouTube Data API v3** — channel + video discovery (quota-managed)
- **Apify** — transcript fetching
- **xUnit + FluentAssertions** — test suite, in-memory SQLite fixtures

## Project layout

```
SkipWatch/                  Blazor Server host — UI, hosted services, DI composition
  Components/               Layout, shared components, pages
  Features/Channels/        Add/list/remove followed channels
  Features/Topics/          Add/list/remove saved YouTube search topics
  Services/Discovery/       CollectionRoundService — cron-driven background round
SkipWatch.Core/             Domain library — referenced by host and tests
  Db/                       DbContext, EF Core migrations (incl. FTS5)
  Entities/                 Channel, Topic, Video, Library, Project, ...
  Services/Discovery/       ChannelDiscoveryRunner (per-channel logic)
  Services/YouTube/         YouTube API client + quota manager
  Services/Transcripts/     Apify transcript source
SkipWatch.Tests/            xUnit test project
docs/                       PRD, phase plans, setup notes
```

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A [YouTube Data API v3](https://console.cloud.google.com/apis/library/youtube.googleapis.com) key
- An [Apify](https://apify.com/) API token (for transcripts)

### Configure secrets

Secrets flow through the standard .NET configuration chain (`appsettings.json` → user-secrets in Development → environment variables). Set the required keys via user-secrets:

```bash
cd SkipWatch
dotnet user-secrets set "YouTube:ApiKey" "AIza..."
dotnet user-secrets set "Apify:Token"    "apify_api_..."
```

### Run

```bash
dotnet run --project SkipWatch/SkipWatch.csproj
```

On first run, SkipWatch creates `~/.skipwatch/` (database + wiki working directory) and applies all EF Core migrations. The hosted `CollectionRoundService` then ticks on the schedule defined in `appsettings.json` (`Discovery:Cron`, default `*/30 * * * *`).

The liveness probe is at `GET /health`.

## Configuration

All knobs live in `SkipWatch/appsettings.json`:

| Section | Key | Default | Notes |
|---|---|---|---|
| `YouTube` | `DailyQuotaLimit` | `10000` | Hard daily quota on the API key |
| `YouTube` | `CeilingUnits` | `9000` | Soft ceiling SkipWatch refuses to cross |
| `Apify` | `RunTimeoutSeconds` | `300` | Per-transcript timeout |
| `Apify` | `PreferredLanguage` | `en` | Transcript language preference |
| `Discovery` | `Cron` | `*/30 * * * *` | Round cadence |
| `Discovery` | `ChannelsPerRound` | `5` | Channels visited per tick |
| `Discovery` | `InitialVideoCap` / `RollingVideoCap` | `20` / `10` | First-time vs. steady-state per-channel cap |
| `Discovery` | `MinVideoDurationSeconds` | `180` | Drops Shorts |
| `Discovery` | `MaxVideoDurationMinutes` | `60` | Drops over-long videos |

## Build & test

```bash
dotnet build SkipWatch.slnx
dotnet test  SkipWatch.slnx
```

The repo enforces `TreatWarningsAsErrors=true` (`Directory.Build.props`). CI runs build + test on every push (`.github/workflows/ci.yml`).

## Status

Active early-stage development. Shipped:

- **Phase 0** — solution skeleton, DbContext, FTS5 migration, health endpoint, CI
- **Phase 1** — channel discovery round (cron-driven `CollectionRoundService` + `ChannelDiscoveryRunner`)

In progress / planned (see `docs/phases/`):

- **Phase 1b** — topic discovery (poll saved YouTube searches alongside channels)
- **Phase 2+** — transcript worker, LLM summarization, triage UI, smart-search chat, project synthesis

The single source of truth for scope and sequencing is `docs/prd.md`.
