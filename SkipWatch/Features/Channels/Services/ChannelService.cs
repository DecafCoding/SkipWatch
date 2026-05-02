using Microsoft.EntityFrameworkCore;
using SkipWatch.Core.Db;
using SkipWatch.Core.Entities;
using SkipWatch.Core.Services.Interfaces;
using SkipWatch.Features.Channels.Models;
using SkipWatch.Features.Channels.Utilities;

namespace SkipWatch.Features.Channels.Services;

public sealed class ChannelService : IChannelService
{
    private readonly SkipWatchDbContext _db;
    private readonly IYouTubeApiService _yt;

    public ChannelService(SkipWatchDbContext db, IYouTubeApiService yt)
    {
        _db = db;
        _yt = yt;
    }

    public async Task<List<ChannelDto>> GetAllAsync(CancellationToken ct = default)
    {
        var channels = await _db.Channels
            .OrderByDescending(c => c.AddedAt)
            .ToListAsync(ct);

        return channels.Select(ToDto).ToList();
    }

    public async Task<AddChannelResult> AddAsync(string handleOrUrlOrId, CancellationToken ct = default)
    {
        var normalized = YouTubeChannelInputParser.Normalize(handleOrUrlOrId);
        if (normalized is null)
            return new AddChannelResult(false, null,
                "Couldn't recognize that as a YouTube channel handle, URL, or ID.", false, false);

        var info = await _yt.GetChannelInfoAsync(normalized, ct);
        if (!info.Success)
            return new AddChannelResult(false, null, info.ErrorMessage, false, info.IsQuotaExceeded);

        if (string.IsNullOrEmpty(info.CanonicalChannelId) || string.IsNullOrEmpty(info.UploadsPlaylistId))
            return new AddChannelResult(false, null,
                "Channel resolved but YouTube didn't return an uploads playlist — it can't be polled.",
                false, false);

        var existing = await _db.Channels
            .FirstOrDefaultAsync(c => c.YoutubeChannelId == info.CanonicalChannelId, ct);
        if (existing is not null)
            return new AddChannelResult(false, ToDto(existing),
                $"Already tracking '{existing.Title}'.", true, false);

        var channel = new Channel
        {
            YoutubeChannelId = info.CanonicalChannelId,
            UploadsPlaylistId = info.UploadsPlaylistId,
            Title = info.Title ?? info.CanonicalChannelId,
            Handle = info.Handle,
            ThumbnailUrl = info.ThumbnailUrl
        };

        _db.Channels.Add(channel);
        await _db.SaveChangesAsync(ct);

        return new AddChannelResult(true, ToDto(channel), null, false, false);
    }

    public async Task<bool> RemoveAsync(int channelId, CancellationToken ct = default)
    {
        var channel = await _db.Channels.FindAsync([channelId], ct);
        if (channel is null)
            return false;

        _db.Channels.Remove(channel);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static ChannelDto ToDto(Channel c) => new(
        c.Id,
        c.YoutubeChannelId,
        c.Title,
        c.Handle,
        c.ThumbnailUrl,
        c.Enabled,
        c.AddedAt,
        c.LastCheckAt,
        c.LastCheckError);
}
