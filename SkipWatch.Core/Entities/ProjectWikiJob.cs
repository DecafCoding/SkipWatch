namespace SkipWatch.Core.Entities;

public class ProjectWikiJob
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;
    public WikiJobAction Action { get; set; }
    public WikiJobStatus Status { get; set; } = WikiJobStatus.Queued;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum WikiJobAction
{
    Add,
    Remove
}

public enum WikiJobStatus
{
    Queued,
    Running,
    Done,
    Parked
}
