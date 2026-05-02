namespace SkipWatch.Core.Entities;

public class Project
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }

    public ProjectWikiStatus WikiStatus { get; set; } = ProjectWikiStatus.Idle;
    public DateTime? WikiUpdatedAt { get; set; }

    public ICollection<VideoProject> VideoProjects { get; set; } = new List<VideoProject>();
}

public enum ProjectWikiStatus
{
    Idle,
    Updating,
    Stale,
    Error
}
