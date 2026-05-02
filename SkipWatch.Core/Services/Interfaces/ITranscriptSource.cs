using SkipWatch.Core.Services.Transcripts;

namespace SkipWatch.Core.Services.Interfaces;

/// <summary>
/// Single seam over the transcript provider. The MVP implementation (<c>ApifyTranscriptSource</c>)
/// hits the Apify <c>streamers/youtube-scraper</c> actor; v2 candidates are local Whisper + yt-dlp
/// or a pluggable cloud provider, all behind this same one-method interface.
/// See <c>prd.md</c> §8 (Risks) for the rationale on keeping the surface narrow.
/// </summary>
public interface ITranscriptSource
{
    Task<Transcript> FetchAsync(string videoId, CancellationToken ct = default);
}
