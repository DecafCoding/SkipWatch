namespace SkipWatch.Features.Topics.Models;

public sealed record AddTopicResult(
    bool Success,
    TopicDto? Topic,
    string? ErrorMessage,
    bool IsDuplicate);
