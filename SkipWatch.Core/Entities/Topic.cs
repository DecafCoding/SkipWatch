namespace SkipWatch.Core.Entities;

public class Topic
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Query { get; set; }
    public bool Enabled { get; set; } = true;
    public int LookbackDays { get; set; } = 7;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckAt { get; set; }
    public string? LastCheckError { get; set; }

    public ICollection<TopicVideo> TopicVideos { get; set; } = new List<TopicVideo>();
}
