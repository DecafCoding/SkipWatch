namespace SkipWatch.Core.Entities;

public class Channel
{
    public int Id { get; set; }
    public required string YoutubeChannelId { get; set; }
    public required string UploadsPlaylistId { get; set; }
    public required string Title { get; set; }
    public string? Handle { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckAt { get; set; }
    public string? LastCheckError { get; set; }

    public ICollection<Video> Videos { get; set; } = new List<Video>();
}
