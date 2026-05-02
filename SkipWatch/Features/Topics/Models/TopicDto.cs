using SkipWatch.Core.Services.Utilities;

namespace SkipWatch.Features.Topics.Models;

public sealed record TopicDto(
    int Id,
    string Name,
    string Query,
    bool Enabled,
    int LookbackDays,
    DateTime AddedAt,
    DateTime? LastCheckAt,
    string? LastCheckError)
{
    public string LastCheckedDisplay =>
        LastCheckAt.HasValue ? FormatHelper.FormatUpdateDateDisplay(LastCheckAt) : "never checked";
}
