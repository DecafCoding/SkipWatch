namespace SkipWatch.Core.Entities;

public class ActivityEntry
{
    public int Id { get; set; }
    public required string Kind { get; set; }       // 'round_start' | 'channel_visit' | 'transcript' | 'summary' | 'wiki'
    public int? RefId { get; set; }                  // ChannelId | VideoId | ProjectId, depending on Kind
    public required string Outcome { get; set; }     // 'ok' | 'fail' | 'skipped_short' | 'skipped_too_long' | 'parked'
    public string? Detail { get; set; }
    public int? DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
