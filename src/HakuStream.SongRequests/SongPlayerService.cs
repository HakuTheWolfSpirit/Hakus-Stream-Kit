using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HakuStream.SongRequests;

public sealed class SongPlayerService(
    SongPlayer player,
    SongQueue queue,
    BackupPlaylist backup,
    AudioDownloader downloader,
    SongTitleStore titles,
    SongSettings settings,
    ILogger<SongPlayerService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, byte> _downloading = new(StringComparer.OrdinalIgnoreCase);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        player.Skip();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadBackupPlaylistAsync(stoppingToken);
        await PredownloadBackupSongsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(500, stoppingToken);

            if (!player.CanPlayNextSong()) continue;

            var next = GetNextSong();
            if (next is null) continue;

            player.Play(next);
        }
    }

    public bool AddCurrentSongToBackup()
    {
        var current = player.Current;
        return current is not null && backup.Add(current.VideoId);
    }

    private QueuedSong? GetNextSong()
    {
        if (queue.TryDequeue(out var song) && song is not null) return song;

        var backupVideoId = backup.GetNext();
        if (backupVideoId is null) return null;

        if (!downloader.FileExists(backupVideoId))
        {
            QueueBackgroundDownload(backupVideoId);
            return null;
        }

        var title = titles.GetTitle(backupVideoId) ?? backupVideoId;
        return new QueuedSong(backupVideoId, title, "Backup Playlist", downloader.GetFilePath(backupVideoId),
            DateTime.UtcNow);
    }

    private void QueueBackgroundDownload(string videoId)
    {
        if (!_downloading.TryAdd(videoId, 0)) return;

        logger.LogInformation("Queuing background download for backup song: {VideoId}", videoId);

        _ = Task.Run(async () =>
        {
            try
            {
                if (await downloader.DownloadAsync(videoId) is null)
                {
                    logger.LogWarning("Background download failed, removing from playlist: {VideoId}", videoId);
                    backup.Remove(videoId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background download error for {VideoId}", videoId);
                backup.Remove(videoId);
            }
            finally
            {
                _downloading.TryRemove(videoId, out _);
            }
        });
    }

    private async Task LoadBackupPlaylistAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(settings.BackupPlaylistUrl)) return;

        try
        {
            await backup.LoadFromPlaylistAsync(settings.BackupPlaylistUrl, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load backup playlist from {Url}", settings.BackupPlaylistUrl);
        }
    }

    private async Task PredownloadBackupSongsAsync(CancellationToken ct)
    {
        var missing = backup.GetAllVideoIds().Where(id => !downloader.FileExists(id)).ToList();
        if (missing.Count == 0) return;

        logger.LogInformation("Downloading {Count} missing backup playlist songs", missing.Count);

        foreach (var videoId in missing)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (await downloader.DownloadAsync(videoId, ct) is null)
                {
                    logger.LogWarning("Failed to download backup song {VideoId}; removing it", videoId);
                    backup.Remove(videoId);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error downloading backup song {VideoId}", videoId);
            }
        }

        logger.LogInformation("Backup playlist pre-download complete");
    }
}
