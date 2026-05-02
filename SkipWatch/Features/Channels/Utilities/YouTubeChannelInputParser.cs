using System.Text.RegularExpressions;

namespace SkipWatch.Features.Channels.Utilities;

/// <summary>
/// Normalizes user-provided channel inputs (URLs, handles, raw IDs) to a single
/// string that <c>IYouTubeApiService.GetChannelInfoAsync</c> can resolve. Returns null
/// when the input does not look like anything we can pass to channels.list.
/// Single-shot resolution — no search step, no disambiguation UI.
/// </summary>
public static class YouTubeChannelInputParser
{
    private static readonly Regex ChannelIdInUrl = new(@"youtube\.com/channel/(UC[a-zA-Z0-9_-]{22})", RegexOptions.IgnoreCase);
    private static readonly Regex HandleInUrl = new(@"youtube\.com/@([a-zA-Z0-9_.\-]+)", RegexOptions.IgnoreCase);
    private static readonly Regex CustomUrlInUrl = new(@"youtube\.com/c/([a-zA-Z0-9_.\-]+)", RegexOptions.IgnoreCase);
    private static readonly Regex UserUrlInUrl = new(@"youtube\.com/user/([a-zA-Z0-9_.\-]+)", RegexOptions.IgnoreCase);
    private static readonly Regex CanonicalChannelId = new(@"^UC[a-zA-Z0-9_-]{22}$");

    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        input = input.Trim();

        // Already a canonical channel ID.
        if (CanonicalChannelId.IsMatch(input))
            return input;

        // Already a bare handle.
        if (input.StartsWith('@'))
            return input;

        // youtube.com/channel/UC...
        var channelIdMatch = ChannelIdInUrl.Match(input);
        if (channelIdMatch.Success)
            return channelIdMatch.Groups[1].Value;

        // youtube.com/@handle
        var handleMatch = HandleInUrl.Match(input);
        if (handleMatch.Success)
            return "@" + handleMatch.Groups[1].Value;

        // youtube.com/c/customname  /  youtube.com/user/legacyname
        // The Data API's forHandle path handles modern customs; legacy /user/ may not
        // resolve, but trying is cheap (1 quota unit) and the user gets a clear error.
        var customMatch = CustomUrlInUrl.Match(input);
        if (customMatch.Success)
            return "@" + customMatch.Groups[1].Value;

        var userMatch = UserUrlInUrl.Match(input);
        if (userMatch.Success)
            return "@" + userMatch.Groups[1].Value;

        // Plain word (e.g. "Fireship") — let GetChannelInfoAsync try forHandle.
        if (!input.Contains('/') && !input.Contains(' '))
            return "@" + input;

        return null;
    }
}
