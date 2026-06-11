using System.Collections.Concurrent;

namespace HakuStream.Kit.Twitch;

public sealed record CooldownResult(bool Active);

public sealed class CooldownService
{
    private readonly ConcurrentDictionary<string, DateTime> _readyAt = new();

    public CooldownResult Check(Type commandType, string userId, IReadOnlyList<CooldownAttribute> cooldowns)
    {
        var now = DateTime.UtcNow;

        foreach (var cooldown in cooldowns)
            if (_readyAt.TryGetValue(Key(commandType, userId, cooldown.Scope), out var readyAt) && now < readyAt)
                return new CooldownResult(true);

        return new CooldownResult(false);
    }

    public void Record(Type commandType, string userId, IReadOnlyList<CooldownAttribute> cooldowns)
    {
        var now = DateTime.UtcNow;
        foreach (var cooldown in cooldowns)
            _readyAt[Key(commandType, userId, cooldown.Scope)] = now + cooldown.Duration;
    }

    private static string Key(Type commandType, string userId, CooldownScope scope)
    {
        return scope switch
        {
            CooldownScope.PerUser => $"{commandType.FullName}:user:{userId}",
            CooldownScope.PerCommand => $"{commandType.FullName}:all",
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }
}
