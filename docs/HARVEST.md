# SkipWatch Harvest Plan

A phased plan for lifting code from `TargetBrowse/` into a new `SkipWatch/` repo aligned with `prd.md`. Each phase ends with something buildable and testable. Companion to `prd.md`'s 8-phase build plan — the harvest happens **inside** prd Phase 0 and Phase 1, before the workers are wired up.

**Order matters.** Dependencies flow downward: skeleton → theme → shared infra → entities → services → pages. Don't lift a Razor page before its services compile.

**Refit-on-lift, not later.** Every harvested file gets its TargetBrowse-isms stripped *as it lands* in SkipWatch — `UserId` removed, `ApplicationDbContext` swapped, rating bits deleted. Don't lift dirty and clean later; the pile compounds.

**Architecture: Vertical Slice with shared services** (same shape as TargetBrowse).
- Feature-self-contained folders: `SkipWatch/Features/<Feature>/{Components, Services, Models, Utilities}`. Each slice owns its Razor components, `IXService` + `XService`, DTO records, and feature-local helpers.
- Shared services live outside the slice: `SkipWatch/Services/` (UI-adjacent — theme, message center) and `SkipWatch.Core/Services/` (server-side — YouTube, transcripts, utilities). Entities + DbContext live in `SkipWatch.Core/{Entities, Db}/`.
- App + Core project split keeps `SkipWatch.Core` UI-free so future workers can reference it without dragging in Blazor.
- **No per-feature repository.** `XService` uses `SkipWatchDbContext` directly. **No CQRS / MediatR / AutoMapper.** Services expose explicit methods; manual `ToDto` mapping; result-type records (e.g. `AddChannelResult`) for expected outcomes instead of exceptions.
- New features after H4 should match this shape — Topics in H5 will look just like Channels in H4: same five-folder layout, same `IXService` + `XService` + `XDto` + `AddXResult` shapes.

---

## Phase H0 — Repo skeleton bring-up (½ day) ✅ DONE

A vanilla Blazor skeleton already exists at [SkipWatch/SkipWatch/](SkipWatch/SkipWatch/) — `dotnet new blazor` output on **.NET 10**, with a `.slnx` solution at [SkipWatch/SkipWatch.slnx](SkipWatch/SkipWatch.slnx). Goal of H0 is to bring it in line with the prd: clean out template debris, add the missing class library + packages, switch Bootstrap to CDN, and stub the DbContext.

**Note on .NET version.** The skeleton targets `net10.0`; `prd.md` §3 says .NET 9. Stay on .NET 10 (current and aligned with the existing skeleton) and update `prd.md` §3 to match — see the "PRD patches" section at the bottom of this file.

**What's already there (don't redo)**
- `SkipWatch.csproj` with `Microsoft.NET.Sdk.Web`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `BlazorDisableThrowNavigationException`.
- `Program.cs` wired with `AddRazorComponents().AddInteractiveServerComponents()`, `MapStaticAssets()`, `MapRazorComponents<App>().AddInteractiveServerRenderMode()`.
- `Components/App.razor`, `Routes.razor`, `_Imports.razor`.
- `Components/Layout/MainLayout.razor` (+ `.razor.css`), `NavMenu.razor` (+ `.razor.css`), `ReconnectModal.razor` (+ `.razor.css`, `.razor.js`).
- `wwwroot/app.css`, `wwwroot/favicon.png`, `wwwroot/lib/bootstrap/`.

**Steps**

1. **Delete template debris.**
   - [Components/Pages/Counter.razor](SkipWatch/SkipWatch/Components/Pages/Counter.razor)
   - [Components/Pages/Weather.razor](SkipWatch/SkipWatch/Components/Pages/Weather.razor)
   - Keep `Home.razor`, `Error.razor`, `NotFound.razor` for now (will be replaced/edited in H1).
   - Remove the Counter/Weather links from `Components/Layout/NavMenu.razor` (will be fully replaced by the harvested NavMenu in H1).

2. **Switch Bootstrap to CDN** (matching TargetBrowse's pattern in [TargetBrowse/Components/App.razor](TargetBrowse/Components/App.razor)).
   - Delete the local copy: `rm -r wwwroot/lib/bootstrap/`.
   - Edit [Components/App.razor](SkipWatch/SkipWatch/Components/App.razor) `<head>`:
     - Replace `<link rel="stylesheet" href="@Assets[\"lib/bootstrap/dist/css/bootstrap.min.css\"]" />` with the CDN link + SRI hash from TargetBrowse:
       ```html
       <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.7/dist/css/bootstrap.min.css" rel="stylesheet"
             integrity="sha384-LN+7fdVzj6u52u30Kp6M/trliBMCMKTyK833zpbD+pXdCLuTusPj697FH4R/5mcr"
             crossorigin="anonymous">
       <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.13.1/font/bootstrap-icons.min.css">
       ```
   - Add the Bootstrap JS bundle CDN script before `</body>`:
     ```html
     <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/js/bootstrap.bundle.min.js"
             integrity="sha384-FKyoEForCGlyvwx9Hj09JcYn3nv7wiPVlz7YYwJrWVcXK/BmnVDxM+D2scQbITxI"
             crossorigin="anonymous"></script>
     ```
   - Defer the theme-bootstrap inline `<script>` and `@ServerThemeAttributes` cookie wiring to **H1** — they ship together with `ThemeService` and the harvested layout.

3. **Add the SkipWatch.Core class library.**
   - `dotnet new classlib -n SkipWatch.Core -o SkipWatch.Core --framework net10.0` from the `SkipWatch/` folder (sibling to the App project).
   - Add reference from `SkipWatch.csproj`: `<ProjectReference Include="..\SkipWatch.Core\SkipWatch.Core.csproj" />`.
   - Add `SkipWatch.Core` to the `.slnx` (open `SkipWatch.slnx`, add the project entry — or `dotnet sln SkipWatch.slnx add SkipWatch.Core/SkipWatch.Core.csproj`).

4. **Add `Directory.Build.props` at the SkipWatch repo root** (`c:/Repos/Personal/SkipWatch/`):
   ```xml
   <Project>
     <PropertyGroup>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <LangVersion>latest</LangVersion>
     </PropertyGroup>
   </Project>
   ```
   This propagates the strict settings to both projects without duplication.

5. **NuGet packages on `SkipWatch.Core`:**
   - `Microsoft.EntityFrameworkCore.Sqlite`
   - `Microsoft.EntityFrameworkCore.Design`
   - `Markdig`

6. **`dotnet-ef` as a local tool** (run from `SkipWatch/` repo root):
   ```bash
   dotnet new tool-manifest
   dotnet tool install dotnet-ef
   ```

7. **Stub `SkipWatchDbContext`** in `SkipWatch.Core/Db/SkipWatchDbContext.cs` — empty `DbSet`s for now, real entities land in H3.

8. **Wire EF Core into `Program.cs`:**
   - Resolve the data dir: `Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".skipwatch")`. Create it if missing.
   - `builder.Services.AddDbContext<SkipWatchDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(dataDir, \"skipwatch.db\")}"));`
   - After `var app = builder.Build();`, run `using var scope = app.Services.CreateScope(); scope.ServiceProvider.GetRequiredService<SkipWatchDbContext>().Database.Migrate();` — no-op until H3 adds a migration.

**Validation**
- `dotnet build` clean with `TreatWarningsAsErrors`.
- `dotnet run` serves the placeholder page; Bootstrap loads from the CDN (check Network tab — no `wwwroot/lib/bootstrap/` requests).
- `~/.skipwatch/` directory exists; `skipwatch.db` is created (empty) on first run.

---

## Phase H1 — Theme + shell (1 day) ✅ DONE

Goal: SkipWatch looks and feels like TargetBrowse — dark theme, top menu, left sidebar, message center — with no functional features yet. **Theme is light/dark only — no `auto` mode.** Default is dark.

**Lift (in this order)**
1. `wwwroot/app.css`, `wwwroot/css/`, `wwwroot/images/`, `favicon.png` → same paths in SkipWatch.
2. [Services/ThemeService.cs](TargetBrowse/Services/ThemeService.cs) → `SkipWatch.App/Services/ThemeService.cs`. Register as scoped in `Program.cs`. **Strip the `auto` enum value / branch.**
3. [Services/MessageCenterService.cs](TargetBrowse/Services/MessageCenterService.cs) → `SkipWatch.App/Services/MessageCenterService.cs`. Register as scoped.
4. Any message-center component(s) under [Components/Shared/](TargetBrowse/Components/Shared/) → `Components/Shared/`.
5. Any theme-toggle component (the `ThemeSelector` referenced from [TargetBrowse/Components/App.razor](TargetBrowse/Components/App.razor)) → `Components/Shared/`. Reduce to a two-button or toggle UI (light / dark) — drop the third "auto" option.
6. Theme bootstrapping in `App.razor`: lift the relevant blocks from [TargetBrowse/Components/App.razor](TargetBrowse/Components/App.razor) and simplify:
   - **Inline FOUC-prevention `<script>`**: replace the three-branch block with a two-branch block. Default to `dark` when no cookie/localStorage is set. Rename the storage key to `skipwatch-theme`.
   - **`window.skipwatchSetTheme(theme)`** (renamed from `tbSetTheme`): handle only `light` / `dark`. Always set `data-bs-theme` on `<html>` and the `theme-light` / `theme-dark` class. Drop the `theme-auto` class entirely.
   - **`window.skipwatchRegisterThemeListener`** (renamed from `tbRegisterThemeListener`): keep as-is.
   - **`enhancedload` re-apply handler**: keep, simplified to two branches.
   - **`@ServerThemeAttributes` cookie reader**: keep, but the switch becomes `"light" => light`, `_ => dark` (dark is the default fallback for missing/tampered cookies). Cookie name becomes `skipwatch-theme`.
7. [Components/Layout/NavMenu.razor](TargetBrowse/Components/Layout/NavMenu.razor) (+ `.razor.css`) → `Components/Layout/`. Edit nav links to point at SkipWatch routes (`/`, `/channels`, `/topics`, `/projects`, `/settings`).
8. [Components/Layout/MainLayout.razor](TargetBrowse/Components/Layout/MainLayout.razor) (+ `.razor.css`) → `Components/Layout/`.

**Refit-on-lift**
- Remove `@inject UserManager<>`, `AuthenticationStateProvider`, `<AuthorizeView>`, login/logout links from every layout/component touched.
- Remove any "daily limit" / "10 summaries left" notification logic from `MessageCenterService` — that was tied to the OpenAI cap which is gone.
- Strip `UserId` from `MessageCenterService`'s message store (it becomes a singleton in-memory queue).
- Grep `wwwroot/css/` and `wwwroot/app.css` for any `theme-auto` selectors and delete the rule blocks. Light/dark-only means the auto-mode CSS is dead code.
- Replace `targetbrowse-theme` with `skipwatch-theme` everywhere it appears (cookie name, localStorage key, JS function names).

**Validation:** Run the app and see the dark theme load with no flash. Toggle to light and back; the choice persists across reloads (cookie + localStorage). Top menu, left sidebar, and message center all render. Trigger a test message; it appears in the message center.

---

## Phase H2 — Shared YouTube infrastructure (1 day) ✅ DONE (with deviations — see below)

Goal: `YouTubeQuotaManager` and the shared YouTube client compile and answer simple requests. Required before harvesting Channels or Topics.

**Lift**
1. [Services/YouTube/DurationParser.cs](TargetBrowse/Services/YouTube/DurationParser.cs) → `SkipWatch.Core/Services/YouTube/`.
2. [Services/YouTube/YouTubeQuotaManager.cs](TargetBrowse/Services/YouTube/YouTubeQuotaManager.cs) → `SkipWatch.Core/Services/YouTube/`. Register as **singleton**.
3. [Services/YouTube/YouTubeApiService.cs](TargetBrowse/Services/YouTube/YouTubeApiService.cs) → `SkipWatch.Core/Services/YouTube/`.
4. [Services/YouTube/SharedYouTubeService.cs](TargetBrowse/Services/YouTube/SharedYouTubeService.cs) → `SkipWatch.Core/Services/YouTube/`.
5. Utilities: [Services/Utilities/FormatHelper.cs](TargetBrowse/Services/Utilities/FormatHelper.cs), [ThumbnailFormatter.cs](TargetBrowse/Services/Utilities/ThumbnailFormatter.cs), [TextFormatter.cs](TargetBrowse/Services/Utilities/TextFormatter.cs), [CssClassFormatter.cs](TargetBrowse/Services/Utilities/CssClassFormatter.cs), [SrtConverter.cs](TargetBrowse/Services/Utilities/SrtConverter.cs) → `SkipWatch.Core/Services/Utilities/`.

**Refit-on-lift**
- `YouTubeQuotaManager`: add the `YOUTUBE_DAILY_QUOTA_CEILING` knob (default 9000) per `prd.md` §6 Phase 1b. Add a `TryReserveAsync(int units)` path that returns false past the ceiling.
- API key now read via the standard .NET configuration chain (`appsettings.json` + user-secrets in dev + env vars). User-secrets is initialized on the App project (`<UserSecretsId>` in csproj) and the key set with `dotnet user-secrets set "YouTube:ApiKey" "AIza..."`.
- Strip any quota-bookkeeping rows that wrote to a SQL Server table; in-memory + a small JSON file under `~/.skipwatch/` is enough for a single-user app.

**Validation:** A throwaway minimal-API endpoint `/debug/yt/channel/{id}` calls `channels.list` via the harvested service and returns the channel title + uploads-playlist ID. Quota counter increments.

---

## Phase H3 — Core entities + DbContext (½ day) ✅ DONE

Goal: `dotnet ef migrations add Initial && dotnet ef database update` produces `~/.skipwatch/skipwatch.db` with the right tables. No FTS5 yet (that's prd Phase 0 / late H3).

**Author from `prd.md` §5 (don't lift)**
- `Channel`, `Video`, `Library`, `Project`, `VideoProject`, `ProjectWikiJob`, `ActivityEntry`, plus the new `Topic` and `TopicVideo` entities.
- Enums: `VideoStatus`, `DecisionSignal`, `ProjectWikiStatus`, `WikiJobAction`, `WikiJobStatus`.

**Reference from TargetBrowse (don't copy verbatim)**
- [Data/Entities/ChannelEntity.cs](TargetBrowse/Data/Entities/ChannelEntity.cs) — for field shapes; drop `UserId`, drop rating fields.
- [Data/Entities/VideoEntity.cs](TargetBrowse/Data/Entities/VideoEntity.cs) — for field shapes; the new `Video` is significantly different (has `Status`, `RetryCount`, `Parked`, etc.).
- [Data/Entities/TopicEntity.cs](TargetBrowse/Data/Entities/TopicEntity.cs) — refit to the new `Topic` entity (no user FK; add `Query`, `LookbackDays`).
- [Data/Entities/TopicVideoEntity.cs](TargetBrowse/Data/Entities/TopicVideoEntity.cs) — refit to provenance-only `TopicVideo` (composite PK).
- [Data/Configurations/](TargetBrowse/Data/Configurations/) — fluent-API patterns are reusable as a reference even though most config in SkipWatch lives in `OnModelCreating` directly.

**Then**
- Add the FTS5 hand-edited migration as described in `prd.md` §5.

**Validation:** `dotnet ef database update` creates the SQLite file; `sqlite3 ~/.skipwatch/skipwatch.db ".schema"` shows the expected tables, indexes, FTS5 virtual table, and triggers.

---

## Phase H4 — Channels feature (1–2 days) ✅ DONE (with deviations — see below)

Goal: user can add a channel by handle/URL, see it in a list, and the channel resolves through `ChannelYouTubeService`. No discovery round yet (that's prd Phase 1).

**Lift services first**
1. [Features/Channels/Services/ChannelMappingService.cs](TargetBrowse/Features/Channels/Services/ChannelMappingService.cs)
2. [Features/Channels/Services/ChannelYouTubeService.cs](TargetBrowse/Features/Channels/Services/ChannelYouTubeService.cs)
3. [Features/Channels/Services/ChannelOnboardingService.cs](TargetBrowse/Features/Channels/Services/ChannelOnboardingService.cs)
4. [Features/Channels/Services/ChannelService.cs](TargetBrowse/Features/Channels/Services/ChannelService.cs)
5. Their `I*.cs` interfaces.

**Then components**
6. [Features/Channels/Components/ChannelSearch.razor](TargetBrowse/Features/Channels/Components/ChannelSearch.razor) (+ `.cs`, `.css`)
7. [Features/Channels/Components/ChannelCard.razor](TargetBrowse/Features/Channels/Components/ChannelCard.razor) (+ `.cs`)
8. [Features/Channels/Components/ChannelList.razor](TargetBrowse/Features/Channels/Components/ChannelList.razor) (+ `.cs`, `.css`)
9. [Features/Channels/Components/Channels.razor](TargetBrowse/Features/Channels/Components/Channels.razor) (+ `.cs`, `.css`) — page route `/channels`.

**Drop**
- [ChannelRatingDisplay.razor](TargetBrowse/Features/Channels/Components/ChannelRatingDisplay.razor) (+ `.cs`, `.css`)
- [ChannelRatingModal.razor](TargetBrowse/Features/Channels/Components/ChannelRatingModal.razor) (+ `.cs`, `.css`)
- `ChannelRatingService.cs` / `IChannelRatingService.cs`

**Refit-on-lift (every file)**
- Strip `UserId` parameters and FK joins from service methods. `IChannelService.AddAsync(string handle)` instead of `AddAsync(string userId, string handle)`.
- Remove `<ChannelRatingDisplay>` from `ChannelCard.razor`. Remove the rating column from `ChannelList.razor`.
- Replace `ApplicationDbContext` injection with `SkipWatchDbContext`.
- `ChannelOnboardingService` keeps its uploads-playlist-ID capture logic — that's load-bearing for prd Phase 1.

**Validation:** Navigate to `/channels`, add `@Fireship`, see the channel card render with title, handle, thumbnail. Row exists in `channels` table with `UploadsPlaylistId` populated.

---

## Phase H5 — Topics feature (1 day) ✅ DONE (with deviations — see below)

Goal: user can add a topic with a search query, see it listed. No topic round yet.

**Lift**
1. [Features/Topics/Services/](TargetBrowse/Features/Topics/Services/) — all services + interfaces.
2. [Features/Topics/Models/](TargetBrowse/Features/Topics/Models/).
3. [Features/Topics/Components/AddTopic.razor](TargetBrowse/Features/Topics/Components/AddTopic.razor) (+ `.cs`, `.css`)
4. [Features/Topics/Components/TopicList.razor](TargetBrowse/Features/Topics/Components/TopicList.razor) (+ `.cs`, `.css`)
5. [Features/Topics/Components/Topics.razor](TargetBrowse/Features/Topics/Components/Topics.razor) (+ `.cs`) — page route `/topics`.

**Refit-on-lift**
- The TargetBrowse `TopicEntity` had a one-liner topic name. The new `Topic` (per `prd.md` §5) has `Name`, `Query`, `Enabled`, `LookbackDays`, `LastCheckAt`, `LastCheckError`. Update `AddTopic.razor` to capture `Query` and `LookbackDays` (slider, default 7).
- Strip `UserId` everywhere.
- Remove any "topic relevance score" / weighted-rank UI — Topics no longer feed a scoring algorithm.

**Validation:** Navigate to `/topics`, add a topic `Postgres internals` with query `postgres internals -shorts`, lookback 14 days. Row exists; UI lists it.

---

## Phase H6 — Transcript source (½ day) ✅ DONE

Goal: a single `ITranscriptSource` abstraction, with the Apify implementation behind it. Required before prd Phase 2.

**Lift**
1. [Services/TranscriptService.cs](TargetBrowse/Services/TranscriptService.cs) → `SkipWatch.Core/Services/Transcripts/ApifyTranscriptSource.cs`.

**Refit-on-lift**
- Wrap behind `ITranscriptSource.FetchAsync(string videoId, CancellationToken ct) -> Task<Transcript>` per `prd.md` §8 risk-mitigation note. Keeps the door open for a v2 local Whisper provider.
- Apify token now read from user-secrets (`dotnet user-secrets set "Apify:Token" "..."`) in development, env vars in shipped form.
- Drop any per-user spend tracking; replace with a single in-memory counter or activity-log row.
- Output shape: timestamped lines `[mm:ss] text` per `prd.md` §5 — convert from whatever format TargetBrowse used.

**Validation:** Throwaway `/debug/transcript/{videoId}` endpoint calls `ITranscriptSource.FetchAsync` and returns the timestamped transcript. One Apify call billed.

---

## Phase H7 — Cleanup sweep (½ day) ✅ DONE — clean

Goal: verify nothing rating-shaped, multi-user-shaped, or SQL-Server-shaped survived the lift.

**Grep checklist (run all on the new SkipWatch repo)**
- `UserId`, `ApplicationUser`, `IdentityUser`, `UserManager`, `SignInManager`, `AuthenticationStateProvider` — should return zero hits.
- `Rating`, `IRatingService`, `RatingHistory`, `1-star` — zero hits.
- `Suggestion`, `ISuggestionService`, `unified scoring`, `dual-source` — zero hits.
- `UserScriptProfile`, `ScriptContent`, `ScriptPromptBuilder`, `ScriptGenerationService` — zero hits (deferred to v2).
- `ApplicationDbContext`, `LocalDB`, `MSSQLLocalDB`, `SqlServer` — zero hits.
- `OpenAI`, `gpt-4o-mini`, `daily limit` — zero hits in code (env knobs renamed too).

**Sanity tests**
- `dotnet build` clean with `TreatWarningsAsErrors`.
- `dotnet ef migrations script` produces SQLite-compatible SQL only.
- Add a channel, add a topic, see them both in the DB. The shell, message center, and dark theme work.

---

## What's not harvested (build fresh per `prd.md`)

These are new in SkipWatch and have no TargetBrowse counterpart worth lifting:

- `CollectionRoundService`, `TranscriptWorker`, `SummaryWorker`, `WikiWorker` (`prd.md` §4, §6 Phases 1–5).
- `Library` and `Project` triage entities — TargetBrowse's `ProjectEntity` is shaped around the script pipeline, not the wiki worker. Build fresh from `prd.md` §5.
- `ProjectWikiJob` queue and the per-Project Markdown wiki under `<data-dir>/wiki/` (`prd.md` §6 Phase 5).
- `JobEventBus` for the SignalR push channel (`prd.md` §4).
- Ollama / `ISummarizer` / `OllamaSummarizer` (`prd.md` §3, §6 Phase 3).
- FTS5 search box (`prd.md` §6 Phase 6).
- Single-file self-contained packaging (`prd.md` §6 Phase 7).

---

## Time estimate

Roughly **5–7 days** of focused work to land H0 through H7. After that, prd Phase 1 onward starts on a clean foundation with channels, topics, theme, and the shared YouTube/transcript clients already working.
