namespace HakuStream.Kit.Twitch;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
