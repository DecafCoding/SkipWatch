using SkipWatch.Core.Services.YouTube.Models;

namespace SkipWatch.Core.Services.Interfaces;

public interface IYouTubeApiService
{
    Task<bool> IsApiAvailableAsync();
    Task<int> GetEstimatedRemainingQuotaAsync();

    /// <summary>
    /// Resolves a YouTube channel by handle, custom URL, or canonical channel ID, returning
    /// title, canonical channel ID, handle, thumbnail, and the uploads-playlist ID.
    /// Captured once at channel-add time and stored for future polling. Costs 1 quota unit.
    /// </summary>
    Task<ChannelInfoResult> GetChannelInfoAsync(string handleOrId, CancellationToken ct = default);
}

public sealed record ChannelInfoResult(
    bool Success,
    string? CanonicalChannelId,
    string? Title,
    string? Handle,
    string? ThumbnailUrl,
    string? UploadsPlaylistId,
    string? ErrorMessage,
    bool IsQuotaExceeded);
