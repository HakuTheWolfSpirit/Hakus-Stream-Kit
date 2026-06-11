namespace HakuStream.Archipelago;

public sealed class ApPovSlot
{
    public string PovScene { get; set; } = string.Empty;
    public string SharedScene { get; set; } = string.Empty;
    public string NameSource { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public sealed class ArchipelagoSettings
{
    public string OwnName { get; set; } = "YourName";
    public string OwnScene { get; set; } = "AP_POV_Me";
    public string OwnColor { get; set; } = "#008be8";

    public List<ApPovSlot> Slots { get; set; } =
    [
        new() { PovScene = "AP_POV_P2", SharedScene = "AP_SHARED_P2", NameSource = "AP_P2_NAME", Color = "#0d6be6" },
        new() { PovScene = "AP_POV_P3", SharedScene = "AP_SHARED_P3", NameSource = "AP_P3_NAME", Color = "#ef710a" },
        new() { PovScene = "AP_POV_P4", SharedScene = "AP_SHARED_P4", NameSource = "AP_P4_NAME", Color = "#04dd00" }
    ];
}
