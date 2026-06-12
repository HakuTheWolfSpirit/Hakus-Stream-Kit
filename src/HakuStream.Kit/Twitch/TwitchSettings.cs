namespace HakuStream.Kit.Twitch;

public sealed class TwitchSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string BotUsername { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string[] ExtraScopes { get; set; } = [];
}
