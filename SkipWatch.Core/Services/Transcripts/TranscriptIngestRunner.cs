using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkipWatch.Core.Db;
using SkipWatch.Core.Entities;
using SkipWatch.Core.Services.Discovery;
using SkipWatch.Core.Services.Interfaces;

namespace SkipWatch.Core.Services.Transcripts;

public sealed class TranscriptIngestRunner : ITranscriptIngestRunner
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(60);

    private readonly SkipWatchDbContext _db;
    private readonly ITranscriptSource _transcripts;
    private readonly DiscoverySettings _discovery;
    private readonly ILogger<TranscriptIngestRunner> _logger;

    public TranscriptIngestRunner(
        SkipWatchDbContext db,
        ITranscriptSource transcripts,
        IOptions<DiscoverySettings> discovery,
        ILogger<TranscriptIngestRunner> logger)
    {
        _db = db;
        _transcripts = transcripts;
        _discovery = discovery.Value;
        _logger = logger;
    }

    public async Task<TranscriptIngestResult> RunAsync(Video video, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        Transcript transcript;
        try
        {
            transcript = await _transcripts.FetchAsync(video.YoutubeVideoId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transcript fetch threw for video {VideoId}", video.YoutubeVideoId);
            transcript = new Transcript(false, null, null, false, null, null, null, null, null, null, ex.Message);
        }

        sw.Stop();
        var elapsedMs = (int)sw.ElapsedMilliseconds;

        // Failure path: bump retry/park, leave Status=Discovered.
        if (!transcript.Success)
        {
            video.RetryCount++;
            video.LastError = transcript.ErrorMessage ?? "unknown error";

            if (video.RetryCount >= _discovery.MaxRetryAttempts)
            {
                video.Parked = true;
                video.ParkedAt = DateTime.UtcNow;
                video.NextAttemptAt = null;
                _db.Activity.Add(new ActivityEntry
                {
                    Kind = "transcript",
                    RefId = video.Id,
                    Outcome = "parked",
                    Detail = video.LastError,
                    DurationMs = elapsedMs,
                });
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Transcript ingest video {VideoId} ({Yt}): parked (retry={Retry}, parked=True) in {Elapsed}ms",
                    video.Id, video.YoutubeVideoId, video.RetryCount, elapsedMs);
                return new TranscriptIngestResult(TranscriptIngestOutcome.Parked, video.RetryCount, video.LastError, elapsedMs);
            }

            // Backoff: 60s × 2^(RetryCount-1), capped at 1h.
            var backoff = TimeSpan.FromTicks(Math.Min(
                MaxBackoff.Ticks,
                BaseBackoff.Ticks * (long)Math.Pow(2, video.RetryCount - 1)));
            video.NextAttemptAt = DateTime.UtcNow.Add(backoff);
            _db.Activity.Add(new ActivityEntry
            {
                Kind = "transcript",
                RefId = video.Id,
                Outcome = "fail",
                Detail = video.LastError,
                DurationMs = elapsedMs,
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Transcript ingest video {VideoId} ({Yt}): retry (retry={Retry}, parked=False) in {Elapsed}ms",
                video.Id, video.YoutubeVideoId, video.RetryCount, elapsedMs);
            return new TranscriptIngestResult(TranscriptIngestOutcome.Retry, video.RetryCount, video.LastError, elapsedMs);
        }

        // Success: overwrite cheap fields when Apify supplied non-null values.
        if (transcript.Description is not null) video.Description = transcript.Description;
        if (transcript.DurationSeconds is not null) video.DurationSeconds = transcript.DurationSeconds;
        if (transcript.ViewCount is not null) video.ViewCount = transcript.ViewCount;
        if (transcript.LikeCount is not null) video.LikeCount = transcript.LikeCount;
        if (transcript.CommentsCount is not null) video.CommentsCount = transcript.CommentsCount;
        if (!string.IsNullOrEmpty(transcript.ThumbnailUrl)) video.ThumbnailUrl = transcript.ThumbnailUrl;

        if (transcript.HasTranscript)
        {
            video.TranscriptText = transcript.TranscriptText;
            video.TranscriptLang = transcript.TranscriptLang;
            video.HasTranscript = true;
            video.TranscribedAt = DateTime.UtcNow;
            video.Status = VideoStatus.Transcribed;
        }
        else
        {
            video.HasTranscript = false;
            video.TranscriptText = null;
            video.TranscriptLang = null;
            video.Status = VideoStatus.NoTranscript;
        }

        // Status changed -> reset retry state per PRD §6 Phase 2 step 4.
        video.RetryCount = 0;
        video.LastError = null;
        video.NextAttemptAt = null;

        var outcome = video.Status == VideoStatus.Transcribed
            ? TranscriptIngestOutcome.Transcribed
            : TranscriptIngestOutcome.NoTranscript;

        _db.Activity.Add(new ActivityEntry
        {
            Kind = "transcript",
            RefId = video.Id,
            Outcome = outcome == TranscriptIngestOutcome.Transcribed ? "ok" : "no_transcript",
            Detail = null,
            DurationMs = elapsedMs,
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Transcript ingest video {VideoId} ({Yt}): {Outcome} (retry=0, parked=False) in {Elapsed}ms",
            video.Id, video.YoutubeVideoId,
            outcome == TranscriptIngestOutcome.Transcribed ? "transcribed" : "no_transcript",
            elapsedMs);

        return new TranscriptIngestResult(outcome, 0, null, elapsedMs);
    }
}
