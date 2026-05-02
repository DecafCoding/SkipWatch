using SkipWatch.Core.Services.Utilities;

namespace SkipWatch.Features.Channels.Models;

public sealed record ChannelDto(
    int Id,
    string YoutubeChannelId,
    string Title,
    string? Handle,
    string? ThumbnailUrl,
    bool Enabled,
    DateTime AddedAt,
    DateTime? LastCheckAt,
    string? LastCheckError)
{
    public string YouTubeUrl => $"https://www.youtube.com/channel/{YoutubeChannelId}";
    public string LastCheckedDisplay =>
        LastCheckAt.HasValue ? FormatHelper.FormatUpdateDateDisplay(LastCheckAt) : "never checked";
}
