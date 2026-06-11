using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace HakuStream.Kit.Twitch.Infrastructure;

public sealed class TwitchApiClient
{
    public TwitchApiClient(string clientId, string? accessToken = null)
    {
        Api = new TwitchAPI();
        Api.Settings.ClientId = clientId;

        if (!string.IsNullOrEmpty(accessToken)) Api.Settings.AccessToken = accessToken;
    }

    public TwitchAPI Api { get; }

    public void SetAccessToken(string accessToken)
    {
        Api.Settings.AccessToken = accessToken;
    }

    public async Task<User?> GetUserByLoginAsync(string username)
    {
        var response = await Api.Helix.Users.GetUsersAsync(logins: [username]);
        return response.Users.Length > 0 ? response.Users[0] : null;
    }

    public async Task<User?> GetUserByIdAsync(string userId)
    {
        var response = await Api.Helix.Users.GetUsersAsync([userId]);
        return response.Users.Length > 0 ? response.Users[0] : null;
    }
}
