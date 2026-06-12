using Microsoft.Extensions.Logging;

namespace HakuStream.SongRequests;

public sealed class AudioDownloader
{
    private readonly ILogger<AudioDownloader> _logger;
    private readonly SongSettings _settings;
    private readonly YouTubeClient _youtube;

    public AudioDownloader(YouTubeClient youtube, SongSettings settings, ILogger<AudioDownloader> logger)
    {
        _youtube = youtube;
        _settings = settings;
        _logger = logger;
        Directory.CreateDirectory(settings.AudioDirectory);
    }

    public async Task<string?> DownloadAsync(string videoId, CancellationToken ct = default)
    {
        if (FileExists(videoId)) return GetFilePath(videoId);

        var workDir = Path.Combine(_settings.WorkingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var rawPath = await _youtube.DownloadAudioAsync(
                videoId, workDir, _settings.AudioFormat, _settings.AudioQuality, ct);

            if (rawPath is null) return null;

            var outputPath = GetFilePath(videoId);
            File.Move(rawPath, outputPath, overwrite: true);
            return outputPath;
        }
        finally
        {
            CleanupWorkingDirectory(workDir);
        }
    }

    public bool FileExists(string videoId)
    {
        return File.Exists(GetFilePath(videoId));
    }

    public string GetFilePath(string videoId)
    {
        var safeId = VideoIdEncoder.ToFilesystemSafe(videoId);
        return Path.Combine(_settings.AudioDirectory, $"{safeId}.{_settings.AudioFormat}");
    }

    private void CleanupWorkingDirectory(string workDir)
    {
        try
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up working directory {WorkDir}", workDir);
        }
    }
}
