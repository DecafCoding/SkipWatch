using Microsoft.EntityFrameworkCore;
using SkipWatch.Core.Entities;

namespace SkipWatch.Core.Db;

public sealed class SkipWatchDbContext(DbContextOptions<SkipWatchDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<TopicVideo> TopicVideos => Set<TopicVideo>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<VideoProject> VideoProjects => Set<VideoProject>();
    public DbSet<ProjectWikiJob> ProjectWikiJobs => Set<ProjectWikiJob>();
    public DbSet<ActivityEntry> Activity => Set<ActivityEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Enums as strings — keeps the SQLite file human-readable.
        b.Entity<Video>().Property(v => v.Status).HasConversion<string>();
        b.Entity<Video>().Property(v => v.DecisionSignal).HasConversion<string>();
        b.Entity<Project>().Property(p => p.WikiStatus).HasConversion<string>();
        b.Entity<ProjectWikiJob>().Property(j => j.Action).HasConversion<string>();
        b.Entity<ProjectWikiJob>().Property(j => j.Status).HasConversion<string>();

        // Channels
        b.Entity<Channel>().HasIndex(c => c.YoutubeChannelId).IsUnique();
        b.Entity<Channel>().HasIndex(c => new { c.Enabled, c.LastCheckAt })
            .HasDatabaseName("idx_channels_round_pick");

        // Topics
        b.Entity<Topic>().HasIndex(t => new { t.Enabled, t.LastCheckAt })
            .HasDatabaseName("idx_topics_round_pick");

        // TopicVideo join — composite PK
        b.Entity<TopicVideo>().HasKey(tv => new { tv.TopicId, tv.VideoId });
        b.Entity<TopicVideo>().HasIndex(tv => tv.VideoId)
            .HasDatabaseName("idx_topic_videos_video");

        // Videos
        b.Entity<Video>().HasIndex(v => v.YoutubeVideoId).IsUnique();
        b.Entity<Video>().HasIndex(v => new { v.Status, v.PublishedAt })
            .HasDatabaseName("idx_videos_status_published")
            .IsDescending(false, true);
        b.Entity<Video>().HasIndex(v => new { v.ChannelId, v.PublishedAt })
            .HasDatabaseName("idx_videos_channel")
            .IsDescending(false, true);

        // Partial indexes for the worker queues. Both stay tiny because they only contain
        // rows actively waiting on a phase. Workers also filter on NextAttemptAt <= now()
        // to honor exponential backoff.
        b.Entity<Video>()
            .HasIndex(v => new { v.NextAttemptAt, v.IngestedAt })
            .HasDatabaseName("idx_videos_q_transcript")
            .HasFilter("\"Status\" = 'Discovered' AND \"Parked\" = 0");
        b.Entity<Video>()
            .HasIndex(v => new { v.NextAttemptAt, v.TranscribedAt })
            .HasDatabaseName("idx_videos_q_summary")
            .HasFilter("\"Status\" = 'Transcribed' AND \"Parked\" = 0");

        // Libraries / Projects
        b.Entity<Library>().HasIndex(l => l.Name).IsUnique();
        b.Entity<Library>().HasIndex(l => l.Slug).IsUnique();
        b.Entity<Project>().HasIndex(p => p.Name).IsUnique();
        b.Entity<Project>().HasIndex(p => p.Slug).IsUnique();

        // VideoProject join — composite PK
        b.Entity<VideoProject>().HasKey(vp => new { vp.VideoId, vp.ProjectId });

        // Wiki job queue
        b.Entity<ProjectWikiJob>()
            .HasIndex(j => new { j.NextAttemptAt, j.EnqueuedAt })
            .HasDatabaseName("idx_wiki_jobs_q")
            .HasFilter("\"Status\" = 'Queued'");
        b.Entity<ProjectWikiJob>()
            .HasIndex(j => new { j.ProjectId, j.Status })
            .HasDatabaseName("idx_wiki_jobs_project");

        // Activity log
        b.Entity<ActivityEntry>().HasIndex(a => a.CreatedAt)
            .HasDatabaseName("idx_activity_recent")
            .IsDescending();
    }
}
