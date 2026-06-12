using HakuStream.Kit.Storage;
using Microsoft.Extensions.Logging;

namespace HakuStream.SongRequests;

public sealed record QueueEntry(QueuedSong Song, int Slot);

public sealed class SongQueue
{
    private readonly List<QueueEntry> _entries;
    private readonly object _lock = new();
    private readonly JsonStore<List<QueueEntry>> _store;
    private readonly Dictionary<string, int> _userRequestCounts = new(StringComparer.OrdinalIgnoreCase);

    public SongQueue(ILogger<SongQueue> logger)
    {
        _store = new JsonStore<List<QueueEntry>>("songqueue", logger);
        _entries = _store.Load();

        foreach (var entry in _entries)
        {
            _userRequestCounts.TryGetValue(entry.Song.RequestedBy, out var currentMax);
            if (entry.Slot > currentMax) _userRequestCounts[entry.Song.RequestedBy] = entry.Slot;
        }
    }

    public bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count == 0;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryEnqueue(QueuedSong song, out int position)
    {
        lock (_lock)
        {
            if (ContainsVideoInternal(song.VideoId))
            {
                position = -1;
                return false;
            }

            _userRequestCounts.TryGetValue(song.RequestedBy, out var currentCount);
            var slot = currentCount + 1;
            _userRequestCounts[song.RequestedBy] = slot;
            _entries.Add(new QueueEntry(song, slot));
            _store.Save(_entries);

            position = GetOrderedQueue().IndexOf(song) + 1;
            return true;
        }
    }

    public void EnqueueFront(QueuedSong song)
    {
        lock (_lock)
        {
            if (ContainsVideoInternal(song.VideoId)) return;

            _userRequestCounts.TryGetValue(song.RequestedBy, out var currentCount);
            _userRequestCounts[song.RequestedBy] = currentCount + 1;
            _entries.Insert(0, new QueueEntry(song, 0));
            _store.Save(_entries);
        }
    }

    public bool TryDequeue(out QueuedSong? song)
    {
        lock (_lock)
        {
            if (_entries.Count == 0)
            {
                song = null;
                return false;
            }

            var minSlot = _entries.Min(e => e.Slot);
            var index = _entries.FindIndex(e => e.Slot == minSlot);
            var entry = _entries[index];
            _entries.RemoveAt(index);

            DecrementUserSlots(entry.Song.RequestedBy);
            _store.Save(_entries);

            song = entry.Song;
            return true;
        }
    }

    public List<QueuedSong> PeekNext(int count)
    {
        lock (_lock)
        {
            return GetOrderedQueue().Take(count).ToList();
        }
    }

    public bool ContainsVideo(string videoId)
    {
        lock (_lock)
        {
            return ContainsVideoInternal(videoId);
        }
    }

    public QueuedSong? RemoveLastByUser(string username)
    {
        lock (_lock)
        {
            var userEntries = _entries
                .Select((e, i) => (Entry: e, Index: i))
                .Where(x => x.Entry.Song.RequestedBy.Equals(username, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (userEntries.Count == 0) return null;

            var last = userEntries.OrderByDescending(x => x.Entry.Slot).First();
            _entries.RemoveAt(last.Index);
            ReduceUserCount(username);
            _store.Save(_entries);

            return last.Entry.Song;
        }
    }

    public QueuedSong? RemoveByVideoId(string username, string videoId)
    {
        lock (_lock)
        {
            var index = _entries.FindIndex(e =>
                e.Song.RequestedBy.Equals(username, StringComparison.OrdinalIgnoreCase) &&
                e.Song.VideoId.Equals(videoId, StringComparison.OrdinalIgnoreCase));

            if (index == -1) return null;

            var entry = _entries[index];
            _entries.RemoveAt(index);

            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Song.RequestedBy.Equals(username, StringComparison.OrdinalIgnoreCase) &&
                    _entries[i].Slot > entry.Slot)
                    _entries[i] = _entries[i] with { Slot = _entries[i].Slot - 1 };
            }

            ReduceUserCount(username);
            _store.Save(_entries);

            return entry.Song;
        }
    }

    public int Clear()
    {
        lock (_lock)
        {
            var count = _entries.Count;
            _entries.Clear();
            _userRequestCounts.Clear();
            _store.Save(_entries);
            return count;
        }
    }

    private bool ContainsVideoInternal(string videoId)
    {
        return _entries.Any(e => e.Song.VideoId.Equals(videoId, StringComparison.OrdinalIgnoreCase));
    }

    private void DecrementUserSlots(string username)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Song.RequestedBy.Equals(username, StringComparison.OrdinalIgnoreCase))
                _entries[i] = _entries[i] with { Slot = _entries[i].Slot - 1 };
        }

        ReduceUserCount(username);
    }

    private void ReduceUserCount(string username)
    {
        if (!_userRequestCounts.TryGetValue(username, out var count)) return;

        if (count <= 1)
            _userRequestCounts.Remove(username);
        else
            _userRequestCounts[username] = count - 1;
    }

    private List<QueuedSong> GetOrderedQueue()
    {
        var result = new List<QueuedSong>();
        var remaining = _entries.ToList();

        while (remaining.Count > 0)
        {
            var minSlot = remaining.Min(e => e.Slot);
            var index = remaining.FindIndex(e => e.Slot == minSlot);
            result.Add(remaining[index].Song);
            remaining.RemoveAt(index);
        }

        return result;
    }
}
