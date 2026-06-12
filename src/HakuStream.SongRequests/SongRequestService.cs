using Microsoft.Extensions.Logging;

namespace HakuStream.SongRequests;

public sealed class SongRequestService(
    YouTubeClient youtube,
    AudioDownloader downloader,
    SongQueue queue,
    SongTitleStore titles,
    SongSettings settings,
    ILogger<SongRequestService> logger)
{
    private readonly HashSet<string> _inProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _inProgressLock = new();

    public async Task<SongRequestResult> RequestAsync(
        string urlOrQuery,
        string requestedBy,
        bool bypassRules,
        CancellationToken ct = default)
    {
        var videoId = youtube.ExtractVideoId(urlOrQuery);
        VideoInfo? info = null;

        if (videoId is null)
        {
            info = await FindBestMatchAsync(urlOrQuery, bypassRules, ct);
            if (info is null)
                return new SongRequestResult(false, Error: "Couldn't find a matching song on YouTube.");

            videoId = info.Id;
            logger.LogInformation("Search '{Query}' resolved to {VideoId} ({Title}, music: {IsMusic})",
                urlOrQuery, info.Id, info.Title, info.IsMusic);
        }

        lock (_inProgressLock)
        {
            if (queue.ContainsVideo(videoId) || _inProgress.Contains(videoId))
                return new SongRequestResult(false, Error: "This video is already in the queue.");

            _inProgress.Add(videoId);
        }

        try
        {
            return await ProcessAsync(videoId, info, requestedBy, bypassRules, ct);
        }
        finally
        {
            lock (_inProgressLock)
            {
                _inProgress.Remove(videoId);
            }
        }
    }

    private async Task<VideoInfo?> FindBestMatchAsync(string query, bool bypassRules, CancellationToken ct)
    {
        var candidates = await youtube.SearchAsync(query, settings.SearchResults, ct);
        if (candidates is null || candidates.Count == 0) return null;

        VideoInfo? fallback = null;
        foreach (var candidate in candidates)
        {
            var info = await youtube.GetVideoInfoAsync(candidate.Id, ct);
            if (info is null) continue;

            if (!bypassRules && !PassesRules(info, out _)) continue;

            if (info.IsMusic) return info;

            if (bypassRules || !settings.RequireMusic) fallback ??= info;
        }

        return fallback;
    }

    private bool PassesRules(VideoInfo info, out string? error)
    {
        if (info.DurationSeconds < settings.MinDurationSeconds || info.DurationSeconds > settings.MaxDurationSeconds)
        {
            error = $"Songs must be between {settings.MinDurationSeconds / 60.0:0.#} and " +
                    $"{settings.MaxDurationSeconds / 60.0:0.#} minutes long.";
            return false;
        }

        if (info.ViewCount < settings.MinViewCount)
        {
            error = $"Songs need at least {settings.MinViewCount} views.";
            return false;
        }

        if (settings.RequireMusic && !info.IsMusic)
        {
            error = "Only videos YouTube lists as music can be requested.";
            return false;
        }

        error = null;
        return true;
    }

    private async Task<SongRequestResult> ProcessAsync(
        string videoId, VideoInfo? info, string requestedBy, bool bypassRules, CancellationToken ct)
    {
        info ??= await youtube.GetVideoInfoAsync(videoId, ct);
        if (info is null) return new SongRequestResult(false, Error: "Could not retrieve video information.");

        if (!bypassRules && !PassesRules(info, out var error))
            return new SongRequestResult(false, Error: error);

        var filePath = await downloader.DownloadAsync(videoId, ct);
        if (filePath is null) return new SongRequestResult(false, Error: "Download failed.");

        titles.SaveTitle(videoId, info.Title);

        var song = new QueuedSong(videoId, info.Title, requestedBy, filePath, DateTime.UtcNow);
        if (!queue.TryEnqueue(song, out var position))
            return new SongRequestResult(false, Error: "This video is already in the queue.");

        logger.LogInformation("Song request: {Title} by {User} at position {Position}",
            info.Title, requestedBy, position);

        return new SongRequestResult(true, song, position, IsMusic: info.IsMusic);
    }
}
