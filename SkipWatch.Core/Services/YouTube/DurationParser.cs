namespace SkipWatch.Core.Services.YouTube;

public static class DurationParser
{
    /// <summary>Parses YouTube ISO 8601 duration (e.g. "PT4M13S") to seconds. Returns 0 on failure.</summary>
    public static int ParseToSeconds(string? duration)
    {
        try
        {
            if (string.IsNullOrEmpty(duration) || !duration.StartsWith("PT"))
                return 0;

            var timeSpan = System.Xml.XmlConvert.ToTimeSpan(duration);
            return (int)timeSpan.TotalSeconds;
        }
        catch
        {
            return 0;
        }
    }
}
