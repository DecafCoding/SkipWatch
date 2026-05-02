using Microsoft.EntityFrameworkCore;
using SkipWatch.Core.Db;
using SkipWatch.Core.Entities;
using SkipWatch.Features.Topics.Models;

namespace SkipWatch.Features.Topics.Services;

public sealed class TopicService : ITopicService
{
    private readonly SkipWatchDbContext _db;

    public TopicService(SkipWatchDbContext db)
    {
        _db = db;
    }

    public async Task<List<TopicDto>> GetAllAsync(CancellationToken ct = default)
    {
        var topics = await _db.Topics
            .OrderByDescending(t => t.AddedAt)
            .ToListAsync(ct);

        return topics.Select(ToDto).ToList();
    }

    public async Task<AddTopicResult> AddAsync(string name, string query, int lookbackDays, CancellationToken ct = default)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        var trimmedQuery = query?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmedName))
            return new AddTopicResult(false, null, "Topic name is required.", false);
        if (string.IsNullOrEmpty(trimmedQuery))
            return new AddTopicResult(false, null, "Search query is required.", false);
        if (lookbackDays < 1 || lookbackDays > 90)
            return new AddTopicResult(false, null, "Lookback days must be between 1 and 90.", false);

        var existing = await _db.Topics
            .FirstOrDefaultAsync(t => t.Name == trimmedName, ct);
        if (existing is not null)
            return new AddTopicResult(false, ToDto(existing),
                $"A topic named '{existing.Name}' already exists.", true);

        var topic = new Topic
        {
            Name = trimmedName,
            Query = trimmedQuery,
            LookbackDays = lookbackDays
        };

        _db.Topics.Add(topic);
        await _db.SaveChangesAsync(ct);

        return new AddTopicResult(true, ToDto(topic), null, false);
    }

    public async Task<bool> RemoveAsync(int topicId, CancellationToken ct = default)
    {
        var topic = await _db.Topics.FindAsync([topicId], ct);
        if (topic is null)
            return false;

        _db.Topics.Remove(topic);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetEnabledAsync(int topicId, bool enabled, CancellationToken ct = default)
    {
        var topic = await _db.Topics.FindAsync([topicId], ct);
        if (topic is null)
            return false;

        topic.Enabled = enabled;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static TopicDto ToDto(Topic t) => new(
        t.Id,
        t.Name,
        t.Query,
        t.Enabled,
        t.LookbackDays,
        t.AddedAt,
        t.LastCheckAt,
        t.LastCheckError);
}
