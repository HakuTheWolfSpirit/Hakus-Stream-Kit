namespace HakuStream.Kit.Events;

public sealed record ChannelPointRedemptionEvent(
    string RedemptionId,
    string RewardId,
    string RewardTitle,
    int Cost,
    string User,
    string UserId,
    string Channel,
    string ChannelId,
    string? UserInput) : BotEvent;
