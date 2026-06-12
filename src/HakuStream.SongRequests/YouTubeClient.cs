using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HakuStream.SongRequests;

public sealed record VideoInfo(string Id, string Title, long ViewCount, int DurationSeconds, bool IsMusic);

public sealed record PlaylistEntry(string Id, string? Title);

public sealed partial class YouTubeClient(ILogger<YouTubeClient> logger)
{
    private static readonly TimeSpan InfoTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PlaylistTimeout = TimeSpan.FromMinutes(5);

    public string? ExtractVideoId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        if (VideoIdRegex().IsMatch(input)) return input;

        var match = YouTubeUrlRegex().Match(input);
        return match.Success ? match.Groups[1].Value : null;
    }

    public async Task<VideoInfo?> GetVideoInfoAsync(string videoId, CancellationToken ct = default)
    {
        var startInfo = YtDlpStartInfo($"--dump-json --no-download \"https://www.youtube.com/watch?v={videoId}\"");

        using var process = new Process { StartInfo = startInfo };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(InfoTimeout);

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            var output = await outputTask;

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                logger.LogError("yt-dlp failed to get video info: {Error}", error);
                return null;
            }

            return ParseVideoInfo(videoId, output);
        }
        catch (OperationCanceledException)
        {
            await KillProcessAsync(process);
            throw;
        }
    }

    private VideoInfo? ParseVideoInfo(string videoId, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            long viewCount = 0;
            if (root.TryGetProperty("view_count", out var views) && views.ValueKind == JsonValueKind.Number)
                viewCount = views.GetInt64();

            var duration = 0;
            if (root.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number)
                duration = dur.GetInt32();

            return new VideoInfo(videoId, root.GetProperty("title").GetString() ?? "Unknown", viewCount, duration,
                IsMusic(root));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse video info");
            return null;
        }
    }

    public async Task<string?> DownloadAudioAsync(
        string videoId,
        string outputDirectory,
        string audioFormat,
        int audioQuality,
        CancellationToken ct = default)
    {
        var safeId = VideoIdEncoder.ToFilesystemSafe(videoId);
        var outputPattern = Path.Combine(outputDirectory, $"{safeId}.%(ext)s");
        var expectedPath = Path.Combine(outputDirectory, $"{safeId}.{audioFormat}");

        var startInfo = YtDlpStartInfo(
            $"-x --audio-format {audioFormat} --audio-quality {audioQuality} " +
            $"-o \"{outputPattern}\" \"https://www.youtube.com/watch?v={videoId}\"");

        logger.LogInformation("Downloading audio for video {VideoId}", videoId);

        using var process = new Process { StartInfo = startInfo };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DownloadTimeout);

        try
        {
            process.Start();
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                logger.LogError("yt-dlp download failed: {Error}", error);
                CleanupPartialDownload(expectedPath);
                return null;
            }

            if (!File.Exists(expectedPath))
            {
                logger.LogError("Expected output file not found: {Path}", expectedPath);
                return null;
            }

            logger.LogInformation("Download complete: {Path}", expectedPath);
            return expectedPath;
        }
        catch (OperationCanceledException)
        {
            await KillProcessAsync(process);
            CleanupPartialDownload(expectedPath);
            throw;
        }
    }

    private static bool IsMusic(JsonElement root)
    {
        if (root.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
        {
            foreach (var category in categories.EnumerateArray())
            {
                if (category.ValueKind == JsonValueKind.String &&
                    string.Equals(category.GetString(), "Music", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return (root.TryGetProperty("artist", out var artist) &&
                artist.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(artist.GetString())) ||
               (root.TryGetProperty("track", out var track) &&
                track.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(track.GetString()));
    }

    public async Task<List<PlaylistEntry>?> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        var sanitized = query.Replace('"', ' ').Replace('\\', ' ').Trim();
        if (sanitized.Length == 0) return null;

        return await GetPlaylistEntriesAsync($"ytsearch{Math.Max(1, maxResults)}:{sanitized}", ct);
    }

    public async Task<List<PlaylistEntry>?> GetPlaylistEntriesAsync(string playlistUrl, CancellationToken ct = default)
    {
        var startInfo = YtDlpStartInfo($"--flat-playlist --dump-json \"{playlistUrl}\"");

        using var process = new Process { StartInfo = startInfo };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PlaylistTimeout);

        try
        {
            process.Start();

            var entries = new List<PlaylistEntry>();
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cts.Token)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (!doc.RootElement.TryGetProperty("id", out var idElement)) continue;

                    var id = idElement.GetString();
                    if (string.IsNullOrEmpty(id)) continue;

                    string? title = null;
                    if (doc.RootElement.TryGetProperty("title", out var titleElement))
                        title = titleElement.GetString();

                    entries.Add(new PlaylistEntry(id, title));
                }
                catch (JsonException)
                {
                    logger.LogWarning("Failed to parse playlist entry JSON");
                }
            }

            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                logger.LogError("yt-dlp failed to get playlist info: {Error}", error);
                return null;
            }

            logger.LogInformation("Fetched {Count} entries from playlist", entries.Count);
            return entries;
        }
        catch (OperationCanceledException)
        {
            await KillProcessAsync(process);
            throw;
        }
    }

    private static ProcessStartInfo YtDlpStartInfo(string arguments)
    {
        return new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private async Task KillProcessAsync(Process process)
    {
        if (process.HasExited) return;

        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            logger.LogDebug("Killed yt-dlp process");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to kill yt-dlp process");
        }
    }

    private void CleanupPartialDownload(string expectedPath)
    {
        try
        {
            if (File.Exists(expectedPath)) File.Delete(expectedPath);

            var partFile = expectedPath + ".part";
            if (File.Exists(partFile)) File.Delete(partFile);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cleanup partial download");
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9_-]{11}$")]
    private static partial Regex VideoIdRegex();

    [GeneratedRegex(@"(?:youtube\.com\/(?:watch\?v=|embed\/|v\/|shorts\/)|youtu\.be\/)([a-zA-Z0-9_-]{11})")]
    private static partial Regex YouTubeUrlRegex();
}
