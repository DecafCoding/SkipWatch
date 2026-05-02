namespace SkipWatch.Core.Services.Utilities;

public static class FormatHelper
{
    public static string FormatCount(ulong? count) =>
        !count.HasValue ? string.Empty : FormatCountInternal(count.Value);

    public static string FormatCount(int? count) =>
        !count.HasValue ? string.Empty : FormatCountInternal((ulong)count.Value);

    private static string FormatCountInternal(ulong count)
    {
        if (count == 0) return "0";
        var value = (double)count;
        return value switch
        {
            >= 1_000_000_000 => $"{Math.Round(value / 1_000_000_000)}B",
            >= 1_000_000 => $"{Math.Round(value / 1_000_000)}M",
            >= 1_000 => $"{value / 1_000:F1}K",
            _ => count.ToString("N0")
        };
    }

    public static string FormatDateDisplay(DateTime? date)
    {
        if (!date.HasValue) return string.Empty;

        var now = DateTime.UtcNow;
        var timeSpan = now - date.Value;

        return timeSpan.TotalDays switch
        {
            < 1 when timeSpan.TotalHours < 1 => "just now",
            < 1 when timeSpan.TotalHours < 24 => $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours != 1 ? "s" : "")} ago",
            < 7 => $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays != 1 ? "s" : "")} ago",
            < 30 => $"{(int)(timeSpan.TotalDays / 7)} week{((int)(timeSpan.TotalDays / 7) != 1 ? "s" : "")} ago",
            < 365 => $"{(int)(timeSpan.TotalDays / 30)} month{((int)(timeSpan.TotalDays / 30) != 1 ? "s" : "")} ago",
            _ => $"{(int)(timeSpan.TotalDays / 365)} year{((int)(timeSpan.TotalDays / 365) != 1 ? "s" : "")} ago"
        };
    }

    public static string FormatUpdateDateDisplay(DateTime? date)
    {
        if (!date.HasValue) return string.Empty;
        var dateText = FormatDateDisplay(date);
        return string.IsNullOrEmpty(dateText) ? string.Empty : $"updated {dateText}";
    }

    public static string FormatDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return string.Empty;
        try
        {
            var timespan = System.Xml.XmlConvert.ToTimeSpan(duration);
            return FormatDurationInternal((int)timespan.TotalSeconds);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string FormatDuration(int? durationInSeconds)
    {
        if (!durationInSeconds.HasValue || durationInSeconds.Value < 0) return string.Empty;
        return FormatDurationInternal(durationInSeconds.Value);
    }

    private static string FormatDurationInternal(int totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}"
            : $"{minutes}:{seconds:D2}";
    }
}
