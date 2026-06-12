namespace HakuStream.SongRequests;

public sealed class SongSettings
{
    public string AudioDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "songs");
    public string WorkingDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "songs-tmp");
    public string AudioFormat { get; set; } = "mp3";
    public int AudioQuality { get; set; }
    public string? BackupPlaylistUrl { get; set; }
    public int MinViewCount { get; set; } = 10;
    public int MinDurationSeconds { get; set; } = 60;
    public int MaxDurationSeconds { get; set; } = 420;
    public int SearchResults { get; set; } = 5;
    public bool RequireMusic { get; set; }
    public int Volume { get; set; } = 100;
    public string? OutputDevice { get; set; }
}
