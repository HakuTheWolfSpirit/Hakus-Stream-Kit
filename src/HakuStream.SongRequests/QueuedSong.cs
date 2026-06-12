namespace HakuStream.SongRequests;

public sealed record QueuedSong(
    string VideoId,
    string Title,
    string RequestedBy,
    string FilePath,
    DateTime RequestedAt);

public sealed record SongRequestResult(
    bool Success,
    QueuedSong? Song = null,
    int? Position = null,
    string? Error = null);
