namespace HakuStream.Archipelago;

public sealed class ApPovSlot
{
    public string PovScene { get; set; } = string.Empty;
    public string SharedScene { get; set; } = string.Empty;
    public string NameSource { get; set; } = string.Empty;
    public string BrowserSource { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public sealed class ArchipelagoSettings
{
    public const int MaxPovCount = 8;

    private static readonly string[] DefaultColors =
        ["#0d6be6", "#ef710a", "#04dd00", "#f5c211", "#9146ff", "#e91e63", "#00c8c8"];

    public string OwnName { get; set; } = "YourName";
    public string OwnScene { get; set; } = "AP_POV_Me";
    public string OwnSharedScene { get; set; } = "AP_SHARED_Me";
    public string OwnNameSource { get; set; } = "AP_Me_NAME";
    public string OwnAudioSource { get; set; } = "AP_Me_AUDIO";
    public string OwnColor { get; set; } = "#008be8";
    public bool AlwaysShareOwnAudio { get; set; }

    public List<ApPovSlot> Slots { get; set; } =
        [.. Enumerable.Range(2, MaxPovCount - 1).Select(DefaultSlot)];

    public static ApPovSlot DefaultSlot(int playerNumber)
    {
        return new ApPovSlot
        {
            PovScene = $"AP_POV_P{playerNumber}",
            SharedScene = $"AP_SHARED_P{playerNumber}",
            NameSource = $"AP_P{playerNumber}_NAME",
            BrowserSource = $"AP_P{playerNumber}_BROWSER",
            Color = DefaultColors[(playerNumber - 2) % DefaultColors.Length]
        };
    }

    public ApPovSlot SlotFor(int playerNumber)
    {
        var index = playerNumber - 2;
        return index < Slots.Count ? Slots[index] : DefaultSlot(playerNumber);
    }
}
