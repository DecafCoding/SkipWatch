namespace SkipWatch.Core.Services.Transcripts;

/// <summary>
/// Single payload returned by <see cref="ITranscriptSource.FetchAsync"/>. Carries both the
/// transcript itself and the rich metadata Apify returns alongside it (description, duration,
/// counts, thumbnail). Per <c>prd.md</c> §6 Phase 2, the transcript worker overwrites the
/// cheap-field columns on Video with these fresher values in the same DB write.
/// </summary>
public sealed record Transcript(
    bool Success,
    string? TranscriptText,
    string? TranscriptLang,
    bool HasTranscript,
    string? Description,
    int? DurationSeconds,
    long? ViewCount,
    long? LikeCount,
    long? CommentsCount,
    string? ThumbnailUrl,
    string? ErrorMessage);
