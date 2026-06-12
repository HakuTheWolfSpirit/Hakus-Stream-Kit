namespace HakuStream.Shoutouts;

public sealed class ShoutoutSettings
{
    public string Scene { get; set; } = "SHARED_SHOUTOUT";
    public string MediaSource { get; set; } = "ShoutOutMedia";
    public string TextSource { get; set; } = "ShoutOutBroadcaster";
    public int ClipsWithinDays { get; set; } = 365;
    public string Message { get; set; } = "📢 Go check out @{name} over at {url} !";
}
