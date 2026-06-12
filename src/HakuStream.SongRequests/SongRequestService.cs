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
        string urlOrId,
        string requestedBy,
        bool bypassRules,
        CancellationToken ct = default)
    {
        var videoId = youtube.ExtractVideoId(urlOrId);
        if (videoId is null) return new SongRequestResult(false, Error: "That doesn't look like a YouTube link.");

        lock (_inProgressLock)
        {
            if (queue.ContainsVideo(videoId) || _inProgress.Contains(videoId))
                return new SongRequestResult(false, Error: "This video is already in the queue.");

            _inProgress.Add(videoId);
        }

        try
        {
            return await ProcessAsync(videoId, requestedBy, bypassRules, ct);
        }
        finally
        {
            lock (_inProgressLock)
            {
                _inProgress.Remove(videoId);
            }
        }
    }

    private async Task<SongRequestResult> ProcessAsync(
        string videoId, string requestedBy, bool bypassRules, CancellationToken ct)
    {
        var info = await youtube.GetVideoInfoAsync(videoId, ct);
        if (info is null) return new SongRequestResult(false, Error: "Could not retrieve video information.");

        if (!bypassRules)
        {
            if (info.DurationSeconds < settings.MinDurationSeconds || info.DurationSeconds > settings.MaxDurationSeconds)
                return new SongRequestResult(false,
                    Error: $"Songs must be between {settings.MinDurationSeconds / 60.0:0.#} and " +
                           $"{settings.MaxDurationSeconds / 60.0:0.#} minutes long.");

            if (info.ViewCount < settings.MinViewCount)
                return new SongRequestResult(false, Error: $"Songs need at least {settings.MinViewCount} views.");
        }

        var filePath = await downloader.DownloadAsync(videoId, ct);
        if (filePath is null) return new SongRequestResult(false, Error: "Download failed.");

        titles.SaveTitle(videoId, info.Title);

        var song = new QueuedSong(videoId, info.Title, requestedBy, filePath, DateTime.UtcNow);
        if (!queue.TryEnqueue(song, out var position))
            return new SongRequestResult(false, Error: "This video is already in the queue.");

        logger.LogInformation("Song request: {Title} by {User} at position {Position}",
            info.Title, requestedBy, position);

        return new SongRequestResult(true, song, position);
    }
}
