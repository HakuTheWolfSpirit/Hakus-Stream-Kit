using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HakuStream.SongRequests;

public enum PlayerState
{
    Idle,
    Paused,
    Playing
}

public sealed class SongPlayer(SongSettings settings, ILogger<SongPlayer> logger) : IDisposable
{
    private readonly object _lock = new();
    private QueuedSong? _current;
    private IWavePlayer? _output;
    private QueuedSong? _previous;
    private AudioFileReader? _reader;
    private PlayerState _state;
    private float _volume = Math.Clamp(settings.Volume, 0, 100) / 100f;

    public QueuedSong? Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public QueuedSong? Previous
    {
        get
        {
            lock (_lock)
            {
                return _previous;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            StopPlaybackLocked();
        }
    }

    public bool CanPlayNextSong()
    {
        lock (_lock)
        {
            return _state switch
            {
                PlayerState.Idle => true,
                PlayerState.Playing => _output is null || _output.PlaybackState == PlaybackState.Stopped,
                _ => false
            };
        }
    }

    public bool Play(QueuedSong song)
    {
        lock (_lock)
        {
            StopPlaybackLocked();

            try
            {
                StartPlaybackLocked(song);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to play {Title} ({Path})", song.Title, song.FilePath);
                _state = PlayerState.Idle;
                return false;
            }

            _previous = _current;
            _current = song;
            _state = PlayerState.Playing;
        }

        logger.LogInformation("Now playing: {Title} (requested by {User})", song.Title, song.RequestedBy);
        return true;
    }

    public void Skip()
    {
        lock (_lock)
        {
            StopPlaybackLocked();
            _state = PlayerState.Idle;
        }
    }

    public QueuedSong? GoBack()
    {
        QueuedSong previous;
        QueuedSong? interrupted;

        lock (_lock)
        {
            if (_previous is null) return null;

            previous = _previous;
            interrupted = _current;
            StopPlaybackLocked();
            _current = previous;

            try
            {
                StartPlaybackLocked(previous);
                _state = PlayerState.Playing;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to replay {Title} ({Path})", previous.Title, previous.FilePath);
                _state = PlayerState.Idle;
                return interrupted;
            }
        }

        logger.LogInformation("Now playing: {Title} (requested by {User})", previous.Title, previous.RequestedBy);
        return interrupted;
    }

    public void TogglePause()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case PlayerState.Playing when _output?.PlaybackState == PlaybackState.Playing:
                    _output.Pause();
                    _state = PlayerState.Paused;
                    break;
                case PlayerState.Paused when _output is not null:
                    _output.Play();
                    _state = PlayerState.Playing;
                    break;
            }
        }
    }

    public void SetVolume(int percent)
    {
        lock (_lock)
        {
            _volume = Math.Clamp(percent, 0, 100) / 100f;
            if (_reader is not null) _reader.Volume = _volume;
        }
    }

    private void StartPlaybackLocked(QueuedSong song)
    {
        var reader = new AudioFileReader(song.FilePath) { Volume = _volume };
        IWavePlayer output;

        try
        {
            output = CreateOutput();
        }
        catch
        {
            reader.Dispose();
            throw;
        }

        try
        {
            output.Init(reader);
            output.Play();
        }
        catch
        {
            output.Dispose();
            reader.Dispose();
            throw;
        }

        _reader = reader;
        _output = output;
    }

    private void StopPlaybackLocked()
    {
        try
        {
            _output?.Stop();
            _output?.Dispose();
            _reader?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to stop playback cleanly");
        }
        finally
        {
            _output = null;
            _reader = null;
        }
    }

    private IWavePlayer CreateOutput()
    {
        if (string.IsNullOrWhiteSpace(settings.OutputDevice)) return new WaveOutEvent();

        using var enumerator = new MMDeviceEnumerator();
        MMDevice? match = null;
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (match is null && device.FriendlyName.Contains(settings.OutputDevice, StringComparison.OrdinalIgnoreCase))
                match = device;
            else
                device.Dispose();
        }

        if (match is not null) return new WasapiOut(match, AudioClientShareMode.Shared, true, 200);

        logger.LogWarning("No audio output device matches '{Device}'; using the default device", settings.OutputDevice);
        return new WaveOutEvent();
    }
}
