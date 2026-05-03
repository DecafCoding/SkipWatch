using SkipWatch.Core.Entities;

namespace SkipWatch.Core.Services.Discovery;

public interface IChannelDiscoveryRunner
{
    Task<ChannelDiscoveryResult> RunAsync(Channel channel, CancellationToken ct = default);
}

public sealed record ChannelDiscoveryResult(
    int NewDiscovered,
    int SkippedShort,
    int SkippedTooLong,
    bool QuotaExceeded,
    string? Error);
