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
