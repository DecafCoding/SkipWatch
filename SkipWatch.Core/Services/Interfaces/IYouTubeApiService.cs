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

    /// <summary>
    /// One page of a channel's uploads playlist. Costs 1 quota unit per call.
    /// Caller controls paging via <paramref name="pageToken"/>.
    /// </summary>
    Task<UploadsPageResult> ListUploadsPageAsync(
        string uploadsPlaylistId,
        int maxResults,
        string? pageToken,
        CancellationToken ct = default);

    /// <summary>
    /// videos.list enrichment for up to 50 IDs in a single call.
    /// Costs 1 quota unit per call regardless of ID count.
    /// </summary>
    Task<VideoDetailsResult> GetVideoDetailsAsync(
        IReadOnlyCollection<string> videoIds,
        CancellationToken ct = default);
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

public sealed record UploadsPageResult(
    bool Success,
    IReadOnlyList<UploadsPageItem> Items,
    string? NextPageToken,
    string? ErrorMessage,
    bool IsQuotaExceeded);

public sealed record UploadsPageItem(
    string YoutubeVideoId,
    string Title,
    DateTime PublishedAt,
    string? ThumbnailUrl);

public sealed record VideoDetailsResult(
    bool Success,
    IReadOnlyList<VideoDetails> Items,
    string? ErrorMessage,
    bool IsQuotaExceeded);

public sealed record VideoDetails(
    string YoutubeVideoId,
    int? DurationSeconds,
    long? ViewCount,
    long? LikeCount,
    long? CommentsCount);
