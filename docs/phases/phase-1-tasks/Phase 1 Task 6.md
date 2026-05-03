# Phase 1 — Task 6: WIRE DI in `Program.cs` and REMOVE the `/debug/yt/channel` endpoint

You are executing one task from SkipWatch Phase 1 (channel discovery round). The full phase plan is at [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md). This file is self-contained for executing Task 6.

## Prerequisites

Tasks 1-5 complete. `IChannelDiscoveryRunner` + `ChannelDiscoveryRunner` exist (Task 4). `CollectionRoundService` exists (Task 5).

## Working directory

cwd = `c:/Repos/Personal/SkipWatch`.

## Phase context (why this task exists)

The runner and the hosted service must be registered with DI to actually run. The Phase 0 `/debug/yt/channel/{handleOrId}` endpoint was the only production caller of the channel-resolution path; PRD §6 commits to removing the two debug endpoints "once their respective workers land." Phase 1 lands the channel-discovery worker, so this debug endpoint goes now. `/debug/transcript/{videoId}` is left for Phase 2 to remove.

## Files you MUST read before implementing

- [SkipWatch/Program.cs](../../../SkipWatch/SkipWatch/Program.cs) — DI registrations live here. Lines 33-46 are the registration block. Lines 79-107 contain the debug endpoint to remove (with the explanatory comment on lines 79-80). Lines 40-43 already have `AddScoped<IChannelService, ...>` / `AddScoped<ITopicService, ...>` — your new `AddScoped` for `IChannelDiscoveryRunner` goes immediately after them.

## The task

### IMPLEMENT

1. After the existing `AddScoped` registrations for `IChannelService` / `ITopicService` (lines 40-43), add:

   ```csharp
   builder.Services.AddScoped<SkipWatch.Core.Services.Discovery.IChannelDiscoveryRunner,
       SkipWatch.Core.Services.Discovery.ChannelDiscoveryRunner>();
   builder.Services.AddHostedService<SkipWatch.Services.Discovery.CollectionRoundService>();
   ```

2. Delete the entire `app.MapGet("/debug/yt/channel/{handleOrId}", ...)` block (currently lines 79-107 with the explanatory comment on lines 79-80). Leave the `/debug/transcript/{videoId}` block (Phase 2 owns its removal).

### PATTERN

Existing `AddScoped` and `AddSingleton` registrations in `Program.cs`.

### IMPORTS

No new top-level usings required (fully-qualified names used in the registrations to avoid disrupting the existing `using` ordering — `dotnet format` will normalize to whichever style `.editorconfig` enforces).

### GOTCHAS

- `AddHostedService<T>` registers `T` as a singleton automatically — do not also `AddSingleton`. The hosted service uses `IServiceScopeFactory` (always available without registration) to resolve scoped dependencies per round.
- Removing the `/debug/yt/channel` endpoint also makes its `IYouTubeApiService` and `IYouTubeQuotaManager` lambda parameters disappear — this is fine, the services are still registered and still used by `ChannelService` and the new runner.

### VALIDATE

```bash
cd c:/Repos/Personal/SkipWatch
dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
  && grep -q 'AddHostedService<SkipWatch.Services.Discovery.CollectionRoundService>' SkipWatch/Program.cs \
  && grep -q 'IChannelDiscoveryRunner' SkipWatch/Program.cs \
  && ! grep -q '/debug/yt/channel' SkipWatch/Program.cs
```

Exit code must be 0.
