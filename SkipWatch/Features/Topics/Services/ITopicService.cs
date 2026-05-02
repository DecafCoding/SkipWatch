using SkipWatch.Features.Topics.Models;

namespace SkipWatch.Features.Topics.Services;

public interface ITopicService
{
    Task<List<TopicDto>> GetAllAsync(CancellationToken ct = default);
    Task<AddTopicResult> AddAsync(string name, string query, int lookbackDays, CancellationToken ct = default);
    Task<bool> RemoveAsync(int topicId, CancellationToken ct = default);
    Task<bool> SetEnabledAsync(int topicId, bool enabled, CancellationToken ct = default);
}
