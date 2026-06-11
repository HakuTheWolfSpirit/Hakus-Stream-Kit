namespace HakuStream.Kit.Twitch;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequirePermissionAttribute : Attribute
{
    public required PermissionLevel Level { get; init; }
}
