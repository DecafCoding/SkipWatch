using Microsoft.EntityFrameworkCore;
using SkipWatch.Components;
using SkipWatch.Core.Db;
using SkipWatch.Core.Services.Interfaces;
using SkipWatch.Core.Services.Transcripts;
using SkipWatch.Core.Services.YouTube;
using SkipWatch.Core.Services.YouTube.Models;
using SkipWatch.Services;
using SkipWatch.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".skipwatch");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "skipwatch.db");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<SkipWatchDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));

// YouTube API config flows through the standard configuration chain:
// appsettings.json → user-secrets (Development only, auto-loaded by CreateBuilder when
// UserSecretsId is set in the csproj) → environment variables.
// To set the API key in dev:  dotnet user-secrets set "YouTube:ApiKey" "AIza..."
builder.Services.Configure<YouTubeApiSettings>(builder.Configuration.GetSection("YouTube"));
builder.Services.Configure<ApifySettings>(builder.Configuration.GetSection("Apify"));

builder.Services.AddSingleton<IYouTubeQuotaManager, YouTubeQuotaManager>();
builder.Services.AddSingleton<IYouTubeApiService, YouTubeApiService>();

// HttpClient lifetime is managed by IHttpClientFactory; ApifyTranscriptSource itself is
// scoped (one per request) so the SignalR circuit can cancel an in-flight call cleanly.
builder.Services.AddHttpClient<ITranscriptSource, ApifyTranscriptSource>();

builder.Services.AddScoped<SkipWatch.Features.Channels.Services.IChannelService,
    SkipWatch.Features.Channels.Services.ChannelService>();
builder.Services.AddScoped<SkipWatch.Features.Topics.Services.ITopicService,
    SkipWatch.Features.Topics.Services.TopicService>();

builder.Services.AddScoped<IThemeService, ThemeService>();
builder.Services.AddSingleton<IMessageCenterService, MessageCenterService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SkipWatchDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Liveness probe. Touches no external dependency — safe to call from the launcher
// script (Phase 7) before the worker pipeline has warmed up.
app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    version = typeof(Program).Assembly.GetName().Version?.ToString(),
    utc = DateTime.UtcNow
}));

// H2 validation endpoint — resolves a channel and returns title + uploads-playlist ID.
// One quota unit per call. Removed once H4 wires the channel-add UI.
app.MapGet("/debug/yt/channel/{handleOrId}", async (
    string handleOrId,
    IYouTubeApiService yt,
    IYouTubeQuotaManager quota,
    CancellationToken ct) =>
{
    var info = await yt.GetChannelInfoAsync(handleOrId, ct);
    var status = await quota.GetQuotaStatusAsync();

    return Results.Json(new
    {
        info.Success,
        info.CanonicalChannelId,
        info.Title,
        info.Handle,
        info.ThumbnailUrl,
        info.UploadsPlaylistId,
        info.ErrorMessage,
        info.IsQuotaExceeded,
        Quota = new
        {
            status.Used,
            status.DailyLimit,
            status.NextReset
        }
    });
});

// H6 validation endpoint — calls Apify for one video and returns the timestamped transcript
// plus the rich metadata. Costs one Apify run (~$0.005). Removed once Phase 2's
// TranscriptWorker is wired up.
app.MapGet("/debug/transcript/{videoId}", async (
    string videoId,
    ITranscriptSource transcripts,
    CancellationToken ct) =>
{
    var t = await transcripts.FetchAsync(videoId, ct);
    return Results.Json(new
    {
        t.Success,
        t.HasTranscript,
        t.TranscriptLang,
        t.DurationSeconds,
        t.ViewCount,
        t.LikeCount,
        t.CommentsCount,
        t.ThumbnailUrl,
        t.ErrorMessage,
        DescriptionPreview = t.Description?.Length > 200 ? t.Description[..200] + "..." : t.Description,
        TranscriptPreview = t.TranscriptText?.Length > 1000 ? t.TranscriptText[..1000] + "..." : t.TranscriptText,
        TranscriptLineCount = t.TranscriptText?.Split('\n').Length ?? 0
    });
});

app.Run();

public partial class Program;
