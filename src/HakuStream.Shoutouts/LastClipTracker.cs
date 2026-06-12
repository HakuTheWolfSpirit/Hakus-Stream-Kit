namespace HakuStream.Shoutouts;

public sealed class LastClipTracker
{
    private volatile string? _lastClipUrl;

    public string? LastClipUrl => _lastClipUrl;

    public void TryRecord(string message)
    {
        if (ShoutoutPlayer.TryExtractClipUrl(message, out var url)) _lastClipUrl = url;
    }
}
