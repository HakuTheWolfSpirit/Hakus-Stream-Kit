using TwitchLib.Client.Events;

namespace HakuStream.Kit.Twitch.Infrastructure;

public sealed class TwitchChatMessageParser
{
    private const char CommandPrefix = '!';

    public ParsedCommand? TryParse(OnMessageReceivedArgs args)
    {
        var msg = args.ChatMessage;
        if (!msg.Message.StartsWith(CommandPrefix)) return null;

        var parts = msg.Message[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        return new ParsedCommand(
            msg.Id,
            parts[0].ToLowerInvariant(),
            parts.Length > 1 ? parts[1..] : [],
            msg.DisplayName,
            msg.UserId,
            msg.Channel,
            msg.UserDetail.IsModerator || msg.IsBroadcaster,
            msg.IsBroadcaster);
    }
}
