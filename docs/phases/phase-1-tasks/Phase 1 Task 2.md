# Phase 1 — Task 2: ADD `NCrontab` package + create `CronSchedule` helper

You are executing one task from SkipWatch Phase 1 (channel discovery round). The full phase plan is at [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md). This file is self-contained for executing Task 2.

## Prerequisite

Task 1 must be complete: `SkipWatch.Core/Services/Discovery/DiscoverySettings.cs` exists, `Discovery` config section is bound in `Program.cs`.

## Working directory

All commands assume cwd = `c:/Repos/Personal/SkipWatch` (the git repo root).

## Phase context (why this task exists)

`CollectionRoundService` (Task 5) needs to wake on a configurable cron schedule. PRD §6 Phase 1 specifies NCrontab parsing for general expressions and a `*/N * * * *` short-circuit that uses `PeriodicTimer(TimeSpan.FromMinutes(N))` directly to avoid drift on the common case. This task wraps both behind a single `CronSchedule` class living in `SkipWatch.Core` so it is unit-testable without booting the host.

## Files you MUST read before implementing

- [SkipWatch.Core/SkipWatch.Core.csproj](../../../SkipWatch/SkipWatch.Core/SkipWatch.Core.csproj) — confirm where `<PackageReference>` entries go.
- [SkipWatch.Core/Services/YouTube/YouTubeApiService.cs](../../../SkipWatch/SkipWatch.Core/Services/YouTube/YouTubeApiService.cs) — confirm the project's namespace conventions and `sealed` class style.

## The task

Wrap NCrontab parsing and the `*/N` shortcut behind a single `CronSchedule` type.

### IMPLEMENT

1. From `c:/Repos/Personal/SkipWatch`, run:

   ```
   dotnet add SkipWatch.Core/SkipWatch.Core.csproj package NCrontab
   ```

   (latest 3.x; pin the version the CLI selects.)

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

### PATTERN

Static `Parse` factory + sealed type, mirroring `Transcript`/`ChannelInfoResult` immutability conventions.

### IMPORTS

`NCrontab` (from the new package), `System.Text.RegularExpressions`.

### GOTCHAS

- NCrontab's default constructor accepts only the standard 5-field format. Do not pass the 6-field (with-seconds) form; the PRD examples are 5-field.
- `FixedInterval` exists so the hosted service (Task 5) can hand the value directly to `new PeriodicTimer(interval)` — Task 5 reads this property to decide which scheduling path to take.

### VALIDATE

```bash
cd c:/Repos/Personal/SkipWatch
dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
  && grep -q 'NCrontab' SkipWatch.Core/SkipWatch.Core.csproj
```

Exit code must be 0.
