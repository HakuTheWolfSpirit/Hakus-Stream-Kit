using System.Reflection;

namespace HakuStream.Kit.Twitch;

public sealed record CommandInfo(
    Type HandlerType,
    PermissionLevel RequiredPermission,
    IReadOnlyList<CooldownAttribute> Cooldowns);

public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandInfo> _byName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => _byName.Keys;

    public void Register(Type handlerType)
    {
        var permission = handlerType.GetCustomAttribute<RequirePermissionAttribute>()?.Level
                         ?? PermissionLevel.Everyone;
        var cooldowns = handlerType.GetCustomAttributes<CooldownAttribute>().ToArray();
        var info = new CommandInfo(handlerType, permission, cooldowns);

        foreach (var name in handlerType.GetCustomAttributes<CommandAttribute>()) _byName[name.Name] = info;
    }

    public bool TryGet(string name, out CommandInfo info)
    {
        return _byName.TryGetValue(name, out info!);
    }
}
