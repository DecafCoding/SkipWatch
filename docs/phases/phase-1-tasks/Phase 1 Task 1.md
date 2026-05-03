# Phase 1 — Task 1: CREATE `DiscoverySettings` and bind it in DI

You are executing one task from SkipWatch Phase 1 (channel discovery round). The full phase plan is at [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md) — read it if you need broader context, but this file is self-contained for executing Task 1.

## Working directory

All commands assume cwd = `c:/Repos/Personal/SkipWatch` (the git repo root). `c:/Repos/Personal/` is **not** a git repo.

## Phase context (why this task exists)

Phase 1 stands up the channel discovery loop: a hosted service (`CollectionRoundService`) wakes on a cron schedule, picks up to N enabled channels not visited in the last 24 hours, and runs a two-call YouTube Data API harvest per channel. Phase-1 behavior is gated by seven configurable constants (cron, channel cap, video caps, duration thresholds, retry attempts). This task creates the typed-options record that holds those constants and binds it to a new `Discovery` config section.

## Validate-before-coding posture

Validate documentation and codebase patterns before implementing. Pay special attention to naming of existing utils, types, and models. Import from the right files.

## Files you MUST read before implementing

- [SkipWatch/Program.cs](../../../SkipWatch/SkipWatch/Program.cs) — DI registrations live here. Lines 30-31 already have `Configure<YouTubeApiSettings>` and `Configure<ApifySettings>`; you will add `Configure<DiscoverySettings>` next to them.
- [SkipWatch/appsettings.json](../../../SkipWatch/SkipWatch/appsettings.json) — confirm the existing `"YouTube"` and `"Apify"` block layout so the new `"Discovery"` block matches.
- [SkipWatch.Core/Services/YouTube/Models/YouTubeApiSettings.cs](../../../SkipWatch/SkipWatch.Core/Services/YouTube/Models/YouTubeApiSettings.cs) — pattern for a typed-options class (sealed, default-valued auto-properties).

## The task

Add the typed-options record holding the seven Phase-1 constants and bind it to a new `Discovery` section in `appsettings.json`.

### IMPLEMENT

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

### PATTERN

`YouTubeApiSettings` and `ApifySettings` (already in `Program.cs` lines 30-31).

### IMPORTS

New `using SkipWatch.Core.Services.Discovery;` in `Program.cs`.

### GOTCHA

The PRD spells the cron knob as the env-var name `SKIPWATCH_ROUND_CRON`. ASP.NET Core's environment-variable provider already remaps double-underscored env vars to colon-separated config keys (`Discovery__Cron` → `Discovery:Cron`). Operators who prefer the PRD's name set `Discovery__Cron` rather than `SKIPWATCH_ROUND_CRON`. Documenting this in the PR body is sufficient; do not introduce a custom env-var alias.

### VALIDATE

```bash
cd c:/Repos/Personal/SkipWatch
dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
  && grep -q '"Discovery"' SkipWatch/appsettings.json \
  && grep -q 'Configure<DiscoverySettings>' SkipWatch/Program.cs
```

Exit code must be 0.
