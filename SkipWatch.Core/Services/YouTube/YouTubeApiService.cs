using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkipWatch.Core.Services.Interfaces;
using SkipWatch.Core.Services.YouTube.Models;

namespace SkipWatch.Core.Services.YouTube;

public class YouTubeApiService : IYouTubeApiService, IDisposable
{
    private readonly Google.Apis.YouTube.v3.YouTubeService _youTubeClient;
    private readonly IYouTubeQuotaManager _quotaManager;
    private readonly YouTubeApiSettings _settings;
    private readonly ILogger<YouTubeApiService> _logger;

    public YouTubeApiService(
        IOptions<YouTubeApiSettings> settings,
        IYouTubeQuotaManager quotaManager,
        ILogger<YouTubeApiService> logger)
    {
        _settings = settings.Value;
        _quotaManager = quotaManager;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException(
                "YouTube API key is not configured. Set YOUTUBE_API_KEY in ~/.skipwatch/.env.");

        _youTubeClient = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
        {
            ApiKey = _settings.ApiKey,
            ApplicationName = "SkipWatch"
        });
    }

    public Task<bool> IsApiAvailableAsync() => _quotaManager.IsApiAvailableAsync();

    public async Task<int> GetEstimatedRemainingQuotaAsync()
    {
        var status = await _quotaManager.GetQuotaStatusAsync();
        return Math.Max(0, _settings.CeilingUnits - status.Used);
    }

    public async Task<ChannelInfoResult> GetChannelInfoAsync(string handleOrId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(handleOrId))
            return Failure("Channel identifier is required");

        if (!await _quotaManager.TryConsumeQuotaAsync(YouTubeApiOperation.GetChannelDetails))
            return new ChannelInfoResult(false, null, null, null, null, null,
                "YouTube API ceiling reached for today. Try again after UTC rollover.", true);

        try
        {
            var request = _youTubeClient.Channels.List("snippet,contentDetails");
            ApplyChannelLookup(request, handleOrId.Trim());
            var response = await request.ExecuteAsync(ct);

            var channel = response?.Items?.FirstOrDefault();
            if (channel is null)
                return Failure($"No channel found for '{handleOrId}'");

            var snippet = channel.Snippet;
            var uploads = channel.ContentDetails?.RelatedPlaylists?.Uploads;

            return new ChannelInfoResult(
                Success: true,
                CanonicalChannelId: channel.Id,
                Title: snippet?.Title,
                Handle: snippet?.CustomUrl,
                ThumbnailUrl: snippet?.Thumbnails?.Default__?.Url
                              ?? snippet?.Thumbnails?.Medium?.Url
                              ?? snippet?.Thumbnails?.High?.Url,
                UploadsPlaylistId: uploads,
                ErrorMessage: null,
                IsQuotaExceeded: false);
        }
        catch (Google.GoogleApiException gex)
        {
            _logger.LogError(gex, "YouTube API error resolving channel '{Input}'", handleOrId);
            var quotaHit = gex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden;
            return new ChannelInfoResult(false, null, null, null, null, null,
                gex.Error?.Message ?? gex.Message, quotaHit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving channel '{Input}'", handleOrId);
            return Failure(ex.Message);
        }
    }

    private static void ApplyChannelLookup(ChannelsResource.ListRequest request, string input)
    {
        // Accept @handle, channel ID (UC...), or legacy username.
        if (input.StartsWith('@'))
            request.ForHandle = input[1..];
        else if (input.StartsWith("UC", StringComparison.Ordinal) && input.Length == 24)
            request.Id = input;
        else
            request.ForHandle = input;
    }

    private static ChannelInfoResult Failure(string message) =>
        new(false, null, null, null, null, null, message, false);

    public void Dispose() => _youTubeClient.Dispose();
}
