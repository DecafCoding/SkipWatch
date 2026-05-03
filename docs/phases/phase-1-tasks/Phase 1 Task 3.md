# Phase 1 — Task 3: EXTEND `IYouTubeApiService` with `ListUploadsPageAsync` and `GetVideoDetailsAsync`

You are executing one task from SkipWatch Phase 1 (channel discovery round). The full phase plan is at [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md). This file is self-contained for executing Task 3.

## Prerequisites

Tasks 1 and 2 complete (`DiscoverySettings`, `CronSchedule`, `NCrontab` package present).

## Working directory

cwd = `c:/Repos/Personal/SkipWatch`.

## Phase context (why this task exists)

The discovery round per channel makes two YouTube Data API calls: `playlistItems.list` against the channel's uploads playlist (1 quota unit) and `videos.list` for `contentDetails.duration` + `statistics` (1 quota unit per page of up to 50 IDs). The existing `IYouTubeApiService` only exposes `GetChannelInfoAsync`. This task adds the two methods the round itself will call, plus the result records that travel with them.

## Files you MUST read before implementing

- [SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs](../../../SkipWatch/SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs) — interface to extend; `ChannelInfoResult` record sits here. New result records (`UploadsPageResult`, `VideoDetailsResult`, `UploadsPageItem`, `VideoDetails`) belong in this same file.
- [SkipWatch.Core/Services/YouTube/YouTubeApiService.cs](../../../SkipWatch/SkipWatch.Core/Services/YouTube/YouTubeApiService.cs) — current `GetChannelInfoAsync` shape (lines 45-91). Mirror its quota gating (`TryConsumeQuotaAsync`), error wrapping (`GoogleApiException` → `IsQuotaExceeded` flag), and `_youTubeClient` reuse.
- [SkipWatch.Core/Services/Interfaces/IYouTubeQuotaManager.cs](../../../SkipWatch/SkipWatch.Core/Services/Interfaces/IYouTubeQuotaManager.cs) — `TryConsumeQuotaAsync(YouTubeApiOperation, int requestCount = 1)` signature. Pass `requestCount = 1` per call.
- [SkipWatch.Core/Services/YouTube/Models/YouTubeApiSettings.cs](../../../SkipWatch/SkipWatch.Core/Services/YouTube/Models/YouTubeApiSettings.cs) — `YouTubeApiOperation.GetPlaylistItems` (cost 1) and `GetVideoDetails` (cost 1) are already defined; do **not** add new operation enum values.
- [SkipWatch.Core/Services/YouTube/DurationParser.cs](../../../SkipWatch/SkipWatch.Core/Services/YouTube/DurationParser.cs) — confirm its public surface and return type before importing. Reuse it; do not write a second parser.

## Reference docs (read before coding)

- YouTube Data API v3 — `playlistItems.list`: `playlistId` parameter, `part=snippet,contentDetails`, `maxResults` (max 50), `pageToken` for paging.
- YouTube Data API v3 — `videos.list`: `id` parameter (comma-separated, up to 50), `part=contentDetails,statistics`.
- The strongly-typed wrappers `_youTubeClient.PlaylistItems.List(...)` and `_youTubeClient.Videos.List(...)` from `Google.Apis.YouTube.v3`. Same shape as `_youTubeClient.Channels.List(...)` already used in `GetChannelInfoAsync`.

## The task

Add two methods + their result records to `IYouTubeApiService`, and implement them in `YouTubeApiService`.

### IMPLEMENT

1. In `SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs`, add to the interface:

   ```csharp
   /// <summary>
   /// One page of a channel's uploads playlist. Costs 1 quota unit per call.
   /// Caller controls paging via <paramref name="pageToken"/>.
   /// </summary>
   Task<UploadsPageResult> ListUploadsPageAsync(
       string uploadsPlaylistId,
       int maxResults,
       string? pageToken,
       CancellationToken ct = default);

   /// <summary>
   /// videos.list enrichment for up to 50 IDs in a single call.
   /// Costs 1 quota unit per call regardless of ID count.
   /// </summary>
   Task<VideoDetailsResult> GetVideoDetailsAsync(
       IReadOnlyCollection<string> videoIds,
       CancellationToken ct = default);
   ```

   and the result records:

   ```csharp
   public sealed record UploadsPageResult(
       bool Success,
       IReadOnlyList<UploadsPageItem> Items,
       string? NextPageToken,
       string? ErrorMessage,
       bool IsQuotaExceeded);

   public sealed record UploadsPageItem(
       string YoutubeVideoId,
       string Title,
       DateTime PublishedAt,
       string? ThumbnailUrl);

   public sealed record VideoDetailsResult(
       bool Success,
       IReadOnlyList<VideoDetails> Items,
       string? ErrorMessage,
       bool IsQuotaExceeded);

   public sealed record VideoDetails(
       string YoutubeVideoId,
       int? DurationSeconds,
       long? ViewCount,
       long? LikeCount,
       long? CommentsCount);
   ```

2. In `SkipWatch.Core/Services/YouTube/YouTubeApiService.cs`, implement both methods. Mirror `GetChannelInfoAsync` for quota gating and exception wrapping. Use:

   - `_youTubeClient.PlaylistItems.List("snippet,contentDetails")` with `PlaylistId = uploadsPlaylistId`, `MaxResults = maxResults`, `PageToken = pageToken`. Map each `Item` to `UploadsPageItem`:
     - `YoutubeVideoId = item.ContentDetails.VideoId`
     - `Title = item.Snippet.Title`
     - `PublishedAt = item.ContentDetails.VideoPublishedAt?.DateTime ?? item.Snippet.PublishedAt?.DateTime ?? DateTime.UtcNow`
     - `ThumbnailUrl = item.Snippet?.Thumbnails?.Default__?.Url ?? Medium ?? High`
   - `_youTubeClient.Videos.List("contentDetails,statistics")` with `Id = string.Join(",", videoIds)`. Use `DurationParser.Parse(item.ContentDetails?.Duration)` to convert ISO-8601 → seconds (read `DurationParser.cs` first to confirm its public surface and return type; if it returns `TimeSpan?`, call `.TotalSeconds` and round). Stats are `ulong?` in the v3 client — cast to `long?`.

### PATTERN

`GetChannelInfoAsync` lines 45-91 — same `TryConsumeQuotaAsync` → `try`/`catch (GoogleApiException)` shape. Reuse the existing `_youTubeClient`.

### IMPORTS

Existing `using Google.Apis.YouTube.v3;` covers the resource types.

### GOTCHAS

- `videos.list` accepts up to 50 IDs per request and is **always 1 unit per call** regardless of how many IDs you pass. Do not split a 30-ID batch into 30 calls.
- When `videoIds` is empty, return `Success = true` with an empty `Items` list **without** consuming a quota unit. The runner can pre-filter to the empty case after intersecting with the DB.
- `DurationParser` is the only pre-existing duration utility — confirm its name and signature with `Read` before importing. If its shape differs from expected, *use it as-is* and do not introduce a parallel parser; if it is genuinely missing, write the parsing inline rather than creating a new file.

### VALIDATE

```bash
cd c:/Repos/Personal/SkipWatch
dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
  && grep -q 'ListUploadsPageAsync' SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs \
  && grep -q 'GetVideoDetailsAsync' SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs \
  && grep -q 'ListUploadsPageAsync' SkipWatch.Core/Services/YouTube/YouTubeApiService.cs \
  && grep -q 'GetVideoDetailsAsync' SkipWatch.Core/Services/YouTube/YouTubeApiService.cs
```

Exit code must be 0.
