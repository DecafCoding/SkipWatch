namespace SkipWatch.Core.Entities;

public class TopicVideo
{
    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}
