using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkipWatch.Core.Db;
using SkipWatch.Core.Entities;
using SkipWatch.Core.Services.Discovery;
using SkipWatch.Core.Services.Interfaces;
using SkipWatch.Core.Services.Transcripts;

namespace SkipWatch.Tests.Services.Transcripts;

public sealed class TranscriptIngestRunnerTests
{
    private static (SqliteConnection conn, SkipWatchDbContext db) NewDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<SkipWatchDbContext>()
            .UseSqlite(conn).Options;
        var db = new SkipWatchDbContext(options);
        db.Database.Migrate();
        return (conn, db);
    }

    private static Channel SeedChannel(SkipWatchDbContext db)
    {
        var ch = new Channel
        {
            YoutubeChannelId = "UC_test",
            UploadsPlaylistId = "UU_test",
            Title = "Test"
        };
        db.Channels.Add(ch);
        db.SaveChanges();
        return ch;
    }

    private static Video SeedVideo(SkipWatchDbContext db, int channelId, string ytId = "yt_v1")
    {
        var v = new Video
        {
            YoutubeVideoId = ytId,
            ChannelId = channelId,
            Title = "T",
            PublishedAt = DateTime.UtcNow,
            Status = VideoStatus.Discovered,
            DurationSeconds = 600,
            ViewCount = 100,
            LikeCount = 10,
            CommentsCount = 1,
        };
        db.Videos.Add(v);
        db.SaveChanges();
        return v;
    }

    private static TranscriptIngestRunner NewRunner(SkipWatchDbContext db, FakeTranscriptSource src, int maxRetry = 3) =>
        new(db, src,
            Options.Create(new DiscoverySettings { MaxRetryAttempts = maxRetry }),
            NullLogger<TranscriptIngestRunner>.Instance);

    [Fact]
    public async Task Success_with_transcript_writes_text_and_status_transcribed()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);
        var v = SeedVideo(db, ch.Id);

        var src = new FakeTranscriptSource();
        src.Responses.Enqueue(new Transcript(true,
            "[00:00] hello\n[00:05] world", "en", true,
            "fresh description", 999, 5000, 200, 30, "https://thumb", null));

        var result = await NewRunner(db, src).RunAsync(v);

        result.Outcome.Should().Be(TranscriptIngestOutcome.Transcribed);
        var saved = db.Videos.Single();
        saved.Status.Should().Be(VideoStatus.Transcribed);
        saved.HasTranscript.Should().BeTrue();
        saved.TranscriptText.Should().Be("[00:00] hello\n[00:05] world");
        saved.TranscriptLang.Should().Be("en");
        saved.TranscribedAt.Should().NotBeNull();
        saved.RetryCount.Should().Be(0);
        saved.LastError.Should().BeNull();
        saved.Description.Should().Be("fresh description");
        saved.DurationSeconds.Should().Be(999);
        saved.ViewCount.Should().Be(5000);
        db.Activity.Single().Outcome.Should().Be("ok");
    }

    [Fact]
    public async Task Success_without_transcript_lands_in_NoTranscript_status()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);
        var v = SeedVideo(db, ch.Id);

        var src = new FakeTranscriptSource();
        src.Responses.Enqueue(new Transcript(true, null, null, false,
            "desc only", 600, null, null, null, null, null));

        var result = await NewRunner(db, src).RunAsync(v);

        result.Outcome.Should().Be(TranscriptIngestOutcome.NoTranscript);
        var saved = db.Videos.Single();
        saved.Status.Should().Be(VideoStatus.NoTranscript);
        saved.HasTranscript.Should().BeFalse();
        saved.TranscriptText.Should().BeNull();
        saved.Description.Should().Be("desc only");
        saved.RetryCount.Should().Be(0);
        db.Activity.Single().Outcome.Should().Be("no_transcript");
    }

    [Fact]
    public async Task Cheap_fields_are_not_overwritten_when_Apify_returns_null()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);
        var v = SeedVideo(db, ch.Id);

        var src = new FakeTranscriptSource();
        src.Responses.Enqueue(new Transcript(true, "[00:00] x", "en", true,
            Description: null, DurationSeconds: null, ViewCount: null,
            LikeCount: null, CommentsCount: null, ThumbnailUrl: null, ErrorMessage: null));

        await NewRunner(db, src).RunAsync(v);

        var saved = db.Videos.Single();
        saved.ViewCount.Should().Be(100);
        saved.DurationSeconds.Should().Be(600);
    }

    [Fact]
    public async Task Failure_increments_retry_and_sets_backoff()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);
        var v = SeedVideo(db, ch.Id);

        var src = new FakeTranscriptSource();
        src.Responses.Enqueue(new Transcript(false, null, null, false,
            null, null, null, null, null, null, "Apify 502 Bad Gateway"));

        var before = DateTime.UtcNow;
        var result = await NewRunner(db, src, maxRetry: 3).RunAsync(v);

        result.Outcome.Should().Be(TranscriptIngestOutcome.Retry);
        var saved = db.Videos.Single();
        saved.Status.Should().Be(VideoStatus.Discovered);
        saved.RetryCount.Should().Be(1);
        saved.LastError.Should().Be("Apify 502 Bad Gateway");
        saved.Parked.Should().BeFalse();
        saved.NextAttemptAt.Should().NotBeNull();
        (saved.NextAttemptAt!.Value - before).TotalSeconds.Should().BeApproximately(60, 5);
        db.Activity.Single().Outcome.Should().Be("fail");
    }

    [Fact]
    public async Task Failure_at_max_retries_parks_the_row()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);
        var v = SeedVideo(db, ch.Id);
        v.RetryCount = 2;
        db.SaveChanges();

        var src = new FakeTranscriptSource();
        src.Responses.Enqueue(new Transcript(false, null, null, false,
            null, null, null, null, null, null, "persistent failure"));

        var result = await NewRunner(db, src, maxRetry: 3).RunAsync(v);

        result.Outcome.Should().Be(TranscriptIngestOutcome.Parked);
        var saved = db.Videos.Single();
        saved.Status.Should().Be(VideoStatus.Discovered);
        saved.Parked.Should().BeTrue();
        saved.ParkedAt.Should().NotBeNull();
        saved.RetryCount.Should().Be(3);
        saved.NextAttemptAt.Should().BeNull();
        db.Activity.Single().Outcome.Should().Be("parked");
    }

    [Fact]
    public async Task Backoff_doubles_each_retry_and_caps_at_one_hour()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);

        for (var prev = 0; prev < 7; prev++)
        {
            var v = SeedVideo(db, ch.Id, ytId: $"yt_step_{prev}");
            v.RetryCount = prev;
            db.SaveChanges();

            var src = new FakeTranscriptSource();
            src.Responses.Enqueue(new Transcript(false, null, null, false,
                null, null, null, null, null, null, "fail"));

            var before = DateTime.UtcNow;
            await NewRunner(db, src, maxRetry: 99).RunAsync(v);

            var saved = db.Videos.Single(x => x.YoutubeVideoId == $"yt_step_{prev}");
            var actual = (saved.NextAttemptAt!.Value - before).TotalSeconds;

            // 60 * 2^(prev) seconds, capped at 3600.
            var expected = Math.Min(60.0 * Math.Pow(2, prev), 3600);
            actual.Should().BeApproximately(expected, expected * 0.1 + 5);
        }
    }

    [Fact]
    public async Task Thrown_exception_is_treated_as_transient_failure()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);
        var v = SeedVideo(db, ch.Id);

        var src = new FakeTranscriptSource { ThrowOnNext = new InvalidOperationException("boom") };

        var result = await NewRunner(db, src).RunAsync(v);

        result.Outcome.Should().Be(TranscriptIngestOutcome.Retry);
        db.Videos.Single().LastError.Should().Be("boom");
    }

    private sealed class FakeTranscriptSource : ITranscriptSource
    {
        public Queue<Transcript> Responses { get; } = new();
        public Exception? ThrowOnNext { get; set; }

        public Task<Transcript> FetchAsync(string videoId, CancellationToken ct = default)
        {
            if (ThrowOnNext is not null)
            {
                var ex = ThrowOnNext;
                ThrowOnNext = null;
                throw ex;
            }
            return Task.FromResult(Responses.Dequeue());
        }
    }
}
