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
