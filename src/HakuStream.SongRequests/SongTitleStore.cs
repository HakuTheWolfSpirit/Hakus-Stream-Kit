using HakuStream.Kit.Storage;
using Microsoft.Extensions.Logging;

namespace HakuStream.SongRequests;

public sealed class SongTitleStore
{
    private readonly object _lock = new();
    private readonly JsonStore<Dictionary<string, string>> _store;
    private readonly Dictionary<string, string> _titles;

    public SongTitleStore(ILogger<SongTitleStore> logger)
    {
        _store = new JsonStore<Dictionary<string, string>>("songtitles", logger);
        _titles = _store.Load();
    }

    public string? GetTitle(string videoId)
    {
        lock (_lock)
        {
            return _titles.GetValueOrDefault(videoId);
        }
    }

    public void SaveTitle(string videoId, string title)
    {
        lock (_lock)
        {
            _titles[videoId] = title;
            _store.Save(_titles);
        }
    }
}
