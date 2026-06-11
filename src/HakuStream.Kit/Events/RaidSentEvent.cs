namespace HakuStream.Kit.Events;

public sealed record RaidSentEvent(
    string FromBroadcasterId,
    string FromBroadcasterLogin,
    string FromBroadcasterName,
    string ToBroadcasterId,
    string ToBroadcasterLogin,
    string ToBroadcasterName,
    int Viewers) : BotEvent;
