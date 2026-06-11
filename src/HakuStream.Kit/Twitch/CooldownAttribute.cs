namespace HakuStream.Kit.Twitch;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CooldownAttribute : Attribute
{
    public required CooldownScope Scope { get; init; }

    public int Seconds { get; init; }

    public int Minutes { get; init; }

    public TimeSpan Duration => TimeSpan.FromSeconds(Seconds) + TimeSpan.FromMinutes(Minutes);
}
