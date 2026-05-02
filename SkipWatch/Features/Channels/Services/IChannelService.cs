using SkipWatch.Features.Channels.Models;

namespace SkipWatch.Features.Channels.Services;

public interface IChannelService
{
    Task<List<ChannelDto>> GetAllAsync(CancellationToken ct = default);
    Task<AddChannelResult> AddAsync(string handleOrUrlOrId, CancellationToken ct = default);
    Task<bool> RemoveAsync(int channelId, CancellationToken ct = default);
}
