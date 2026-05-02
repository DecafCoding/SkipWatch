namespace SkipWatch.Core.Services.Utilities;

public static class ThumbnailFormatter
{
    public static string GetVideoThumbnailUrl(
        string? providedThumbnailUrl,
        string youTubeVideoId,
        ThumbnailQuality quality = ThumbnailQuality.HqDefault) =>
        !string.IsNullOrEmpty(providedThumbnailUrl)
            ? providedThumbnailUrl
            : GetYouTubeThumbnailUrl(youTubeVideoId, quality);

    public static string GetYouTubeThumbnailUrl(string youTubeVideoId, ThumbnailQuality quality = ThumbnailQuality.HqDefault)
    {
        var qualityString = quality switch
        {
            ThumbnailQuality.Default => "default",
            ThumbnailQuality.MqDefault => "mqdefault",
            ThumbnailQuality.HqDefault => "hqdefault",
            ThumbnailQuality.SdDefault => "sddefault",
            ThumbnailQuality.MaxResDefault => "maxresdefault",
            _ => "hqdefault"
        };
        return $"https://img.youtube.com/vi/{youTubeVideoId}/{qualityString}.jpg";
    }
}

public enum ThumbnailQuality
{
    Default,
    MqDefault,
    HqDefault,
    SdDefault,
    MaxResDefault
}
