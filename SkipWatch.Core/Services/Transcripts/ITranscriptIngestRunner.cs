using SkipWatch.Core.Entities;

namespace SkipWatch.Core.Services.Transcripts;

public interface ITranscriptIngestRunner
{
    /// <summary>
    /// Process a single Discovered/Parked=false video row. Always returns a result; never throws
    /// across this boundary except for caller-driven <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<TranscriptIngestResult> RunAsync(Video video, CancellationToken ct = default);
}

public enum TranscriptIngestOutcome
{
    Transcribed,
    NoTranscript,
    Retry,
    Parked,
}

public sealed record TranscriptIngestResult(
    TranscriptIngestOutcome Outcome,
    int RetryCount,
    string? Error,
    int ElapsedMs);
