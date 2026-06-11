namespace HakuStream.Kit.Events;

public abstract record BotEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
