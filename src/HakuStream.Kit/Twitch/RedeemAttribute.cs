namespace HakuStream.Kit.Twitch;

[AttributeUsage(AttributeTargets.Class)]
public sealed class RedeemAttribute(string title) : Attribute
{
    public string Title { get; } = title;

    public int Cost { get; set; }
    public string? Description { get; set; }
    public string? BackgroundColor { get; set; }
    public bool RequiresUserInput { get; set; }

    public bool SkipRequestQueue { get; set; } = true;

    public int GlobalCooldownSeconds { get; set; }
    public int MaxPerUserPerStream { get; set; }

    public bool ExternallyManaged { get; set; }
}
