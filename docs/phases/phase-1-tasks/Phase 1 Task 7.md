# Phase 1 — Task 7: CREATE `CronScheduleTests`

You are executing one task from SkipWatch Phase 1 (channel discovery round). The full phase plan is at [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md). This file is self-contained for executing Task 7.

## Prerequisites

Task 2 complete (`CronSchedule` exists in `SkipWatch.Core/Services/Discovery/`).

## Working directory

cwd = `c:/Repos/Personal/SkipWatch`.

## Phase context (why this task exists)

`CronSchedule.Parse` has two code paths: the `*/N * * * *` regex shortcut that returns a `FixedInterval` (used by `PeriodicTimer`), and the NCrontab fallback that computes next-occurrence delays. Both must keep working — pin them with unit tests so future refactors of `CronSchedule` cannot silently break PRD-mandated behavior.

## Files you MUST read before implementing

- [SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs](../../../SkipWatch/SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs) — xUnit + FluentAssertions style to mirror.
- [SkipWatch.Tests/Usings.cs](../../../SkipWatch/SkipWatch.Tests/Usings.cs) — confirm `Xunit` and `FluentAssertions` are global-using'd; do not redundantly import them.
- `SkipWatch.Core/Services/Discovery/CronSchedule.cs` (Task 2) — the type under test.

## The task

### IMPLEMENT

Create `SkipWatch.Tests/Services/Discovery/CronScheduleTests.cs`:

```csharp
using SkipWatch.Core.Services.Discovery;

namespace SkipWatch.Tests.Services.Discovery;

public sealed class CronScheduleTests
{
    [Theory]
    [InlineData("*/30 * * * *", 30)]
    [InlineData("*/5 * * * *", 5)]
    [InlineData("*/1 * * * *", 1)]
    public void EveryNMinutes_pattern_uses_periodic_timer_shortcut(string expr, int expectedMinutes)
    {
        var schedule = CronSchedule.Parse(expr);
        schedule.FixedInterval.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Theory]
    [InlineData("0 * * * *")]
    [InlineData("15 9 * * *")]
    [InlineData("*/30 9-17 * * *")]
    public void NonShortcut_expressions_use_ncrontab(string expr)
    {
        var schedule = CronSchedule.Parse(expr);
        schedule.FixedInterval.Should().BeNull();
        var anchor = new DateTime(2026, 5, 3, 12, 0, 0, DateTimeKind.Utc);
        schedule.GetDelayFromUtcNow(anchor).Should().BeGreaterThan(TimeSpan.Zero);
    }
}
```

### PATTERN

`SkipWatchDbContextSmokeTests` for xUnit + FluentAssertions style. `Xunit` and `FluentAssertions` come from `SkipWatch.Tests/Usings.cs`.

### GOTCHA

For the non-shortcut anchor `2026-05-03 12:00:00 UTC`, every NCrontab expression in the InlineData rows yields a future occurrence within 24 hours — so the delay is always positive. If a future change wants to test the "next occurrence is right now" boundary, use `BeGreaterThanOrEqualTo` instead.

### VALIDATE

```bash
cd c:/Repos/Personal/SkipWatch
dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj --filter "FullyQualifiedName~CronScheduleTests" --nologo -v quiet
```

Must exit 0 with 6 passing tests.
