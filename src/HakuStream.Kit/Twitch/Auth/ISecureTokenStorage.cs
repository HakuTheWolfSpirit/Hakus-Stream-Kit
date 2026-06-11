namespace HakuStream.Kit.Twitch.Auth;

public sealed record TokenData(string AccessToken, string RefreshToken);

public interface ISecureTokenStorage
{
    TokenData? Load();
    void Save(string accessToken, string refreshToken);
    void Clear();
}
