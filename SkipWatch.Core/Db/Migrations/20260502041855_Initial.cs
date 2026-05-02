using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkipWatch.Core.Db.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Activity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    RefId = table.Column<int>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    YoutubeChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    UploadsPlaylistId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Handle = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCheckError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Libraries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Libraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    WikiStatus = table.Column<string>(type: "TEXT", nullable: false),
                    WikiUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Topics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Query = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LookbackDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCheckError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Topics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Videos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    YoutubeVideoId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ViewCount = table.Column<long>(type: "INTEGER", nullable: true),
                    LikeCount = table.Column<long>(type: "INTEGER", nullable: true),
                    CommentsCount = table.Column<long>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Parked = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParkedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LibraryId = table.Column<int>(type: "INTEGER", nullable: true),
                    TranscriptText = table.Column<string>(type: "TEXT", nullable: true),
                    TranscriptLang = table.Column<string>(type: "TEXT", nullable: true),
                    HasTranscript = table.Column<bool>(type: "INTEGER", nullable: false),
                    TranscribedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SummaryMd = table.Column<string>(type: "TEXT", nullable: true),
                    DecisionSignal = table.Column<string>(type: "TEXT", nullable: true),
                    SummarizedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IngestedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Videos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Videos_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Videos_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProjectWikiJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EnqueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectWikiJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectWikiJobs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectWikiJobs_Videos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "Videos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TopicVideos",
                columns: table => new
                {
                    TopicId = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoId = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicVideos", x => new { x.TopicId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_TopicVideos_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TopicVideos_Videos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "Videos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoProjects",
                columns: table => new
                {
                    VideoId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoProjects", x => new { x.VideoId, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_VideoProjects_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VideoProjects_Videos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "Videos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_activity_recent",
                table: "Activity",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_channels_round_pick",
                table: "Channels",
                columns: new[] { "Enabled", "LastCheckAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_YoutubeChannelId",
                table: "Channels",
                column: "YoutubeChannelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_Name",
                table: "Libraries",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_Slug",
                table: "Libraries",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Slug",
                table: "Projects",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_wiki_jobs_project",
                table: "ProjectWikiJobs",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "idx_wiki_jobs_q",
                table: "ProjectWikiJobs",
                columns: new[] { "NextAttemptAt", "EnqueuedAt" },
                filter: "\"Status\" = 'Queued'");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWikiJobs_VideoId",
                table: "ProjectWikiJobs",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "idx_topics_round_pick",
                table: "Topics",
                columns: new[] { "Enabled", "LastCheckAt" });

            migrationBuilder.CreateIndex(
                name: "idx_topic_videos_video",
                table: "TopicVideos",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_ProjectId",
                table: "VideoProjects",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "idx_videos_channel",
                table: "Videos",
                columns: new[] { "ChannelId", "PublishedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_videos_q_summary",
                table: "Videos",
                columns: new[] { "NextAttemptAt", "TranscribedAt" },
                filter: "\"Status\" = 'Transcribed' AND \"Parked\" = 0");

            migrationBuilder.CreateIndex(
                name: "idx_videos_q_transcript",
                table: "Videos",
                columns: new[] { "NextAttemptAt", "IngestedAt" },
                filter: "\"Status\" = 'Discovered' AND \"Parked\" = 0");

            migrationBuilder.CreateIndex(
                name: "idx_videos_status_published",
                table: "Videos",
                columns: new[] { "Status", "PublishedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Videos_LibraryId",
                table: "Videos",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_Videos_YoutubeVideoId",
                table: "Videos",
                column: "YoutubeVideoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Activity");

            migrationBuilder.DropTable(
                name: "ProjectWikiJobs");

            migrationBuilder.DropTable(
                name: "TopicVideos");

            migrationBuilder.DropTable(
                name: "VideoProjects");

            migrationBuilder.DropTable(
                name: "Topics");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Videos");

            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropTable(
                name: "Libraries");
        }
    }
}
