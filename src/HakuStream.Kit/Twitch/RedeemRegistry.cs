using System.Reflection;

namespace HakuStream.Kit.Twitch;

public sealed record RedeemInfo(Type HandlerType, RedeemAttribute Reward);

public sealed class RedeemRegistry
{
    private readonly Dictionary<string, RedeemInfo> _byTitle = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<RedeemInfo> All => _byTitle.Values;

    public void Register(Type handlerType)
    {
        var reward = handlerType.GetCustomAttribute<RedeemAttribute>()
                     ?? throw new InvalidOperationException($"{handlerType.Name} is missing [Redeem]");

        _byTitle[reward.Title] = new RedeemInfo(handlerType, reward);
    }

    public bool TryGet(string title, out RedeemInfo info)
    {
        return _byTitle.TryGetValue(title, out info!);
    }
}
