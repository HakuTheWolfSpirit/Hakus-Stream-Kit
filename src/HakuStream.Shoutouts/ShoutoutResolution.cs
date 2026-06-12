namespace HakuStream.Shoutouts;

public sealed record ShoutoutResolution(
    string UserId,
    string? Login,
    string DisplayName,
    string? ClipId,
    string? ClipMp4Url,
    double ClipDurationSeconds)
{
    public bool HasClip => !string.IsNullOrEmpty(ClipMp4Url);
}
