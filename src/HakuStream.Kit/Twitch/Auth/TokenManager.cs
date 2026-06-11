namespace HakuStream.Kit.Twitch.Auth;

public sealed class TokenManager(ISecureTokenStorage storage)
{
    private readonly object _lock = new();

    public string AccessToken { get; private set; } = string.Empty;
    public string RefreshToken { get; private set; } = string.Empty;
    public string UserId { get; internal set; } = string.Empty;

    public bool TryLoad()
    {
        try
        {
            var data = storage.Load();
            if (data is null || string.IsNullOrEmpty(data.AccessToken)) return false;

            AccessToken = data.AccessToken;
            RefreshToken = data.RefreshToken;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Save(string accessToken, string refreshToken)
    {
        lock (_lock)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            storage.Save(accessToken, refreshToken);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            AccessToken = string.Empty;
            RefreshToken = string.Empty;
            storage.Clear();
        }
    }
}
