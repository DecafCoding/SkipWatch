using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkipWatch.Core.Db;
using SkipWatch.Core.Entities;
using SkipWatch.Core.Services.Interfaces;

namespace SkipWatch.Core.Services.Discovery;

public sealed class ChannelDiscoveryRunner : IChannelDiscoveryRunner
{
    private readonly SkipWatchDbContext _db;
    private readonly IYouTubeApiService _yt;
    private readonly DiscoverySettings _settings;
    private readonly ILogger<ChannelDiscoveryRunner> _logger;

    public ChannelDiscoveryRunner(
        SkipWatchDbContext db,
        IYouTubeApiService yt,
        IOptions<DiscoverySettings> settings,
        ILogger<ChannelDiscoveryRunner> logger)
    {
        _db = db;
        _yt = yt;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ChannelDiscoveryResult> RunAsync(Channel channel, CancellationToken ct = default)
    {
        var hasExisting = await _db.Videos.AnyAsync(v => v.ChannelId == channel.Id, ct);
        var cap = hasExisting ? _settings.RollingVideoCap : _settings.InitialVideoCap;

        var collected = new List<UploadsPageItem>(cap);
        string? pageToken = null;
        var stopOnExisting = false;

        while (collected.Count < cap)
        {
            var pageSize = Math.Min(50, cap - collected.Count);
            var page = await _yt.ListUploadsPageAsync(channel.UploadsPlaylistId, pageSize, pageToken, ct);

            if (!page.Success)
            {
                if (page.IsQuotaExceeded)
                    return new ChannelDiscoveryResult(0, 0, 0, true, page.ErrorMessage);
                return new ChannelDiscoveryResult(0, 0, 0, false, page.ErrorMessage);
            }

            foreach (var item in page.Items)
            {
                var exists = await _db.Videos.AnyAsync(v => v.YoutubeVideoId == item.YoutubeVideoId, ct);
                if (exists)
                {
                    stopOnExisting = true;
                    break;
                }
                collected.Add(item);
                if (collected.Count >= cap) break;
            }

            if (stopOnExisting) break;
            if (string.IsNullOrEmpty(page.NextPageToken)) break;
            pageToken = page.NextPageToken;
        }

        if (collected.Count == 0)
            return new ChannelDiscoveryResult(0, 0, 0, false, null);

        var ids = collected.Select(i => i.YoutubeVideoId).ToList();
        var details = await _yt.GetVideoDetailsAsync(ids, ct);
        if (!details.Success)
        {
            if (details.IsQuotaExceeded)
                return new ChannelDiscoveryResult(0, 0, 0, true, details.ErrorMessage);
            return new ChannelDiscoveryResult(0, 0, 0, false, details.ErrorMessage);
        }

        var detailsById = details.Items.ToDictionary(d => d.YoutubeVideoId);

        int newDiscovered = 0, skippedShort = 0, skippedTooLong = 0;
        var minSeconds = _settings.MinVideoDurationSeconds;
        var maxSeconds = _settings.MaxVideoDurationMinutes * 60;

        foreach (var item in collected)
        {
            if (!detailsById.TryGetValue(item.YoutubeVideoId, out var d))
                continue;

            VideoStatus status;
            if (d.DurationSeconds is null)
            {
                status = VideoStatus.SkippedTooLong;
                skippedTooLong++;
            }
            else if (d.DurationSeconds.Value <= minSeconds)
            {
                status = VideoStatus.SkippedShort;
                skippedShort++;
            }
            else if (d.DurationSeconds.Value > maxSeconds)
            {
                status = VideoStatus.SkippedTooLong;
                skippedTooLong++;
            }
            else
            {
                status = VideoStatus.Discovered;
                newDiscovered++;
            }

            _db.Videos.Add(new Video
            {
                YoutubeVideoId = item.YoutubeVideoId,
                ChannelId = channel.Id,
                Title = item.Title,
                PublishedAt = item.PublishedAt,
                ThumbnailUrl = item.ThumbnailUrl,
                DurationSeconds = d.DurationSeconds,
                ViewCount = d.ViewCount,
                LikeCount = d.LikeCount,
                CommentsCount = d.CommentsCount,
                Status = status,
            });
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Channel {ChannelId} discovery: +{New} discovered, {Short} short, {Long} too long",
            channel.YoutubeChannelId, newDiscovered, skippedShort, skippedTooLong);

        return new ChannelDiscoveryResult(newDiscovered, skippedShort, skippedTooLong, false, null);
    }
}
