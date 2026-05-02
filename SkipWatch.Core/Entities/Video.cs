namespace SkipWatch.Core.Entities;

public class Video
{
    public int Id { get; set; }
    public required string YoutubeVideoId { get; set; }
    public int ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;
    public required string Title { get; set; }

    // Discovery-time fields (Data API: playlistItems.list + videos.list)
    public DateTime PublishedAt { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public long? ViewCount { get; set; }
    public long? LikeCount { get; set; }
    public long? CommentsCount { get; set; }

    // Filled in by the transcript worker (Apify `text`). Surfaced on the details
    // view alongside SummaryMd, never on the dashboard card.
    public string? Description { get; set; }

    // One property drives pipeline + triage. Stored as TEXT (HasConversion<string>).
    public VideoStatus Status { get; set; } = VideoStatus.Discovered;

    // Per-phase retry state. RetryCount resets to 0 on every status transition.
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public bool Parked { get; set; }
    public DateTime? ParkedAt { get; set; }

    // Triage + transcript + summary
    public int? LibraryId { get; set; }
    public Library? Library { get; set; }
    public string? TranscriptText { get; set; }
    public string? TranscriptLang { get; set; }
    public bool HasTranscript { get; set; }
    public DateTime? TranscribedAt { get; set; }
    public string? SummaryMd { get; set; }
    public DecisionSignal? DecisionSignal { get; set; }
    public DateTime? SummarizedAt { get; set; }
    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VideoProject> VideoProjects { get; set; } = new List<VideoProject>();
    public ICollection<TopicVideo> TopicVideos { get; set; } = new List<TopicVideo>();
}

public enum VideoStatus
{
    Discovered,
    Transcribed,
    Ready,
    SkippedShort,
    SkippedTooLong,
    NoTranscript,
    Libraried,
    Projected,
    Passed
}

public enum DecisionSignal
{
    Watch,
    Skim,
    Skip
}
