using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkipWatch.Core.Db;
using SkipWatch.Core.Entities;
using SkipWatch.Core.Services.Discovery;
using SkipWatch.Core.Services.Interfaces;

namespace SkipWatch.Tests.Services.Discovery;

public sealed class ChannelDiscoveryRunnerTests
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
            YoutubeChannelId = "UC_test_channel",
            UploadsPlaylistId = "UU_test_uploads",
            Title = "Test Channel",
        };
        db.Channels.Add(ch);
        db.SaveChanges();
        return ch;
    }

    private static DiscoverySettings DefaultSettings() => new()
    {
        InitialVideoCap = 20,
        RollingVideoCap = 10,
        MinVideoDurationSeconds = 180,
        MaxVideoDurationMinutes = 60,
    };

    private static ChannelDiscoveryRunner NewRunner(SkipWatchDbContext db, FakeYouTubeApi yt, DiscoverySettings? settings = null) =>
        new(db, yt, Options.Create(settings ?? DefaultSettings()), NullLogger<ChannelDiscoveryRunner>.Instance);

    [Fact]
    public async Task Cold_start_inserts_up_to_initial_cap()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);

        var items = Enumerable.Range(1, 20)
            .Select(i => new UploadsPageItem($"v{i}", $"Title {i}", DateTime.UtcNow, null))
            .ToList();
        var details = items.Select(i => new VideoDetails(i.YoutubeVideoId, 600, 100, 10, 1)).ToList();

        var yt = new FakeYouTubeApi();
        yt.UploadsPages.Enqueue(new UploadsPageResult(true, items, NextPageToken: null, null, false));
        yt.VideoDetailsResponses.Enqueue(new VideoDetailsResult(true, details, null, false));

        var result = await NewRunner(db, yt).RunAsync(ch);

        result.NewDiscovered.Should().Be(20);
        db.Videos.Count().Should().Be(20);
        db.Videos.Where(v => v.Status == VideoStatus.Discovered).Count().Should().Be(20);
    }

    [Fact]
    public async Task Stops_paging_when_existing_video_id_seen()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);

        db.Videos.Add(new Video
        {
            YoutubeVideoId = "v3",
            ChannelId = ch.Id,
            Title = "preexisting",
            PublishedAt = DateTime.UtcNow,
            DurationSeconds = 600,
            Status = VideoStatus.Discovered,
        });
        db.SaveChanges();

        var items = new List<UploadsPageItem>
        {
            new("v1", "v1", DateTime.UtcNow, null),
            new("v2", "v2", DateTime.UtcNow, null),
            new("v3", "v3", DateTime.UtcNow, null),
            new("v4", "v4", DateTime.UtcNow, null),
            new("v5", "v5", DateTime.UtcNow, null),
        };

        var details = new List<VideoDetails>
        {
            new("v1", 600, null, null, null),
            new("v2", 600, null, null, null),
        };

        var yt = new FakeYouTubeApi();
        yt.UploadsPages.Enqueue(new UploadsPageResult(true, items, null, null, false));
        yt.VideoDetailsResponses.Enqueue(new VideoDetailsResult(true, details, null, false));

        var result = await NewRunner(db, yt).RunAsync(ch);

        result.NewDiscovered.Should().Be(2);
        db.Videos.Count().Should().Be(3); // v3 preexisting + v1, v2 inserted
        yt.VideoDetailsCalls.Should().ContainSingle();
        yt.VideoDetailsCalls[0].Should().BeEquivalentTo(new[] { "v1", "v2" }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Duration_gate_places_rows_in_correct_status_buckets()
    {
        var (conn, db) = NewDb();
        using var _ = conn;
        using var __ = db;
        var ch = SeedChannel(db);

        var items = new List<UploadsPageItem>
        {
            new("vshort", "short", DateTime.UtcNow, null),
            new("vok", "ok", DateTime.UtcNow, null),
            new("vlong", "long", DateTime.UtcNow, null),
        };
        var details = new List<VideoDetails>
        {
            new("vshort", 60, null, null, null),
            new("vok", 600, null, null, null),
            new("vlong", 4000, null, null, null),
        };

        var yt = new FakeYouTubeApi();
        yt.UploadsPages.Enqueue(new UploadsPageResult(true, items, null, null, false));
        yt.VideoDetailsResponses.Enqueue(new VideoDetailsResult(true, details, null, false));

        var result = await NewRunner(db, yt).RunAsync(ch);

        result.NewDiscovered.Should().Be(1);
        result.SkippedShort.Should().Be(1);
        result.SkippedTooLong.Should().Be(1);

        db.Videos.Single(v => v.YoutubeVideoId == "vshort").Status.Should().Be(VideoStatus.SkippedShort);
        db.Videos.Single(v => v.YoutubeVideoId == "vok").Status.Should().Be(VideoStatus.Discovered);
        db.Videos.Single(v => v.YoutubeVideoId == "vlong").Status.Should().Be(VideoStatus.SkippedTooLong);
    }

    private sealed class FakeYouTubeApi : IYouTubeApiService
    {
        public Queue<UploadsPageResult> UploadsPages { get; } = new();
        public Queue<VideoDetailsResult> VideoDetailsResponses { get; } = new();
        public List<IReadOnlyList<string>> VideoDetailsCalls { get; } = new();

        public Task<bool> IsApiAvailableAsync() => Task.FromResult(true);
        public Task<int> GetEstimatedRemainingQuotaAsync() => Task.FromResult(int.MaxValue);

        public Task<ChannelInfoResult> GetChannelInfoAsync(string handleOrId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<UploadsPageResult> ListUploadsPageAsync(
            string uploadsPlaylistId, int maxResults, string? pageToken, CancellationToken ct = default)
        {
            return Task.FromResult(UploadsPages.Dequeue());
        }

        public Task<VideoDetailsResult> GetVideoDetailsAsync(
            IReadOnlyCollection<string> videoIds, CancellationToken ct = default)
        {
            VideoDetailsCalls.Add(videoIds.ToList());
            return Task.FromResult(VideoDetailsResponses.Dequeue());
        }
    }
}
