namespace SkipWatch.Core.Entities;

public class VideoProject
{
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
