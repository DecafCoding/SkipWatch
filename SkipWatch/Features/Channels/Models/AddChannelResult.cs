namespace SkipWatch.Features.Channels.Models;

public sealed record AddChannelResult(
    bool Success,
    ChannelDto? Channel,
    string? ErrorMessage,
    bool IsDuplicate,
    bool IsQuotaExceeded);
