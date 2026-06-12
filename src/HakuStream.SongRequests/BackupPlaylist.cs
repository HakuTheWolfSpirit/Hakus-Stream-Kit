using HakuStream.Kit.Storage;
using Microsoft.Extensions.Logging;

namespace HakuStream.SongRequests;

public sealed class BackupPlaylistState
{
    public List<string> VideoIds { get; set; } = [];
    public int CurrentIndex { get; set; }
}

public sealed class BackupPlaylist
{
    private readonly object _lock = new();
    private readonly ILogger<BackupPlaylist> _logger;
    private readonly BackupPlaylistState _state;
    private readonly JsonStore<BackupPlaylistState> _store;
    private readonly SongTitleStore _titles;
    private readonly YouTubeClient _youtube;

    public BackupPlaylist(YouTubeClient youtube, SongTitleStore titles, ILogger<BackupPlaylist> logger)
    {
        _youtube = youtube;
        _titles = titles;
        _logger = logger;
        _store = new JsonStore<BackupPlaylistState>("backupplaylist", logger);
        _state = _store.Load();
    }

    public bool Add(string videoId)
    {
        lock (_lock)
        {
            if (_state.VideoIds.Contains(videoId, StringComparer.OrdinalIgnoreCase)) return false;

            _state.VideoIds.Add(videoId);
            _store.Save(_state);
            return true;
        }
    }

    public bool Remove(string videoId)
    {
        lock (_lock)
        {
            var index = _state.VideoIds.FindIndex(v => v.Equals(videoId, StringComparison.OrdinalIgnoreCase));
            if (index == -1) return false;

            _state.VideoIds.RemoveAt(index);
            if (index < _state.CurrentIndex) _state.CurrentIndex--;

            _store.Save(_state);
            return true;
        }
    }

    public string? GetNext()
    {
        lock (_lock)
        {
            if (_state.VideoIds.Count == 0) return null;

            if (_state.CurrentIndex >= _state.VideoIds.Count) Shuffle();

            var videoId = _state.VideoIds[_state.CurrentIndex];
            _state.CurrentIndex++;
            _store.Save(_state);
            return videoId;
        }
    }

    public IReadOnlyList<string> GetAllVideoIds()
    {
        lock (_lock)
        {
            return [.. _state.VideoIds];
        }
    }

    public async Task<int> LoadFromPlaylistAsync(string playlistUrl, CancellationToken ct = default)
    {
        var entries = await _youtube.GetPlaylistEntriesAsync(playlistUrl, ct);
        if (entries is null || entries.Count == 0) return 0;

        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.Title)) _titles.SaveTitle(entry.Id, entry.Title);
        }

        int added;
        lock (_lock)
        {
            added = 0;
            foreach (var entry in entries)
            {
                if (_state.VideoIds.Contains(entry.Id, StringComparer.OrdinalIgnoreCase)) continue;

                _state.VideoIds.Add(entry.Id);
                added++;
            }

            Shuffle();
            _store.Save(_state);
        }

        _logger.LogInformation("Loaded {Total} videos from playlist, added {Added} new", entries.Count, added);
        return added;
    }

    private void Shuffle()
    {
        for (var i = _state.VideoIds.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (_state.VideoIds[i], _state.VideoIds[j]) = (_state.VideoIds[j], _state.VideoIds[i]);
        }

        _state.CurrentIndex = 0;
    }
}
