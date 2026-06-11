namespace HakuStream.Kit.Events;

public sealed record ChatMessageEvent(
    string MessageId,
    string Message,
    string User,
    string UserId,
    string Channel,
    bool IsModerator,
    bool IsBroadcaster) : BotEvent;
