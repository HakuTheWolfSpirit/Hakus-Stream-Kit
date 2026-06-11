using HakuStream.Kit.Obs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace HakuStream.Archipelago;

public sealed class ArchipelagoObsSetupService(
    ObsClient obs,
    ArchipelagoSettings settings,
    ILogger<ArchipelagoObsSetupService> logger)
{
    public const int MinPovCount = 2;
    public const string DiscordScene = "AP_DISCORD";

    private const int CanvasWidth = 1920;
    private const int CanvasHeight = 1080;
    private const int BorderPx = 18;
    private const int PrimaryWidth = 1440;
    private const int PrimaryHeight = 810;
    private const int ThumbWidth = 480;
    private const int ThumbHeight = 270;

    private static readonly (int X, int Y)[] ThumbnailCells =
    [
        (0, PrimaryHeight),
        (ThumbWidth, PrimaryHeight),
        (2 * ThumbWidth, PrimaryHeight),
        (PrimaryWidth, 0),
        (PrimaryWidth, ThumbHeight),
        (PrimaryWidth, 2 * ThumbHeight),
        (PrimaryWidth, 3 * ThumbHeight)
    ];

    public async Task SetupScenesAsync(int povCount, CancellationToken ct)
    {
        if (povCount is < MinPovCount or > ArchipelagoSettings.MaxPovCount)
        {
            throw new ArgumentOutOfRangeException(nameof(povCount),
                $"POV count must be between {MinPovCount} and {ArchipelagoSettings.MaxPovCount}.");
        }

        var existing = (await obs.GetSceneNamesAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var kinds = await obs.GetInputKindsAsync(ct);
        var povs = BuildPovs(povCount);

        foreach (var pov in povs)
            await EnsureSharedSceneAsync(pov, existing, kinds, ct);

        await EnsureDiscordSceneAsync(existing, kinds, ct);

        foreach (var pov in povs)
            await EnsurePovSceneAsync(pov, povs, existing, ct);

        logger.LogInformation("OBS scene setup complete for {Count} POVs", povCount);
    }

    private List<PovDescriptor> BuildPovs(int povCount)
    {
        var povs = new List<PovDescriptor>
        {
            new("Me", settings.OwnName, settings.OwnScene, settings.OwnSharedScene,
                settings.OwnNameSource, settings.OwnColor, string.Empty, IsOwn: true)
        };

        for (var player = 2; player <= povCount; player++)
        {
            var slot = settings.SlotFor(player);
            povs.Add(new PovDescriptor($"P{player}", $"P{player}", slot.PovScene, slot.SharedScene,
                slot.NameSource, slot.Color, slot.BrowserSource));
        }

        return povs;
    }

    private async Task EnsureSharedSceneAsync(
        PovDescriptor pov, HashSet<string> existing, IReadOnlyList<string> kinds, CancellationToken ct)
    {
        if (!existing.Add(pov.SharedScene))
        {
            logger.LogInformation("Scene '{Scene}' already exists; leaving it untouched", pov.SharedScene);
            return;
        }

        await obs.CreateSceneAsync(pov.SharedScene, ct);

        await obs.CreateInputAsync(pov.SharedScene, $"AP_{pov.Label}_BG", PickKind(kinds, "color_source"),
            new JObject
            {
                ["color"] = ToObsColor(pov.Color),
                ["width"] = CanvasWidth,
                ["height"] = CanvasHeight
            }, ct: ct);

        var windowId = await obs.CreateInputAsync(pov.SharedScene, $"AP_{pov.Label}_WINDOW",
            PickKind(kinds, "window_capture"), new JObject(), enabled: pov.IsOwn, ct: ct);
        await obs.SetSceneItemTransformAsync(pov.SharedScene, windowId, InsetTransform(), ct);

        if (pov.IsOwn)
        {
            await obs.CreateInputAsync(pov.SharedScene, settings.OwnAudioSource,
                PickKind(kinds, "wasapi_process_output_capture"), new JObject(), ct: ct);
        }
        else
        {
            var browserId = await obs.CreateInputAsync(pov.SharedScene, pov.BrowserSource,
                PickKind(kinds, "browser_source"),
                new JObject
                {
                    ["width"] = CanvasWidth - 2 * BorderPx,
                    ["height"] = CanvasHeight - 2 * BorderPx,
                    ["reroute_audio"] = true
                }, enabled: false, ct: ct);
            await obs.SetSceneItemTransformAsync(pov.SharedScene, browserId, InsetTransform(), ct);
            await obs.SetInputMutedAsync(pov.BrowserSource, true, ct);
        }

        var nameId = await obs.CreateInputAsync(pov.SharedScene, pov.NameSource,
            PickKind(kinds, "text_gdiplus", "text_ft2_source"),
            new JObject { ["text"] = pov.DisplayName }, ct: ct);
        await obs.SetSceneItemTransformAsync(pov.SharedScene, nameId, new JObject
        {
            ["positionX"] = 2 * BorderPx,
            ["positionY"] = 2 * BorderPx,
            ["alignment"] = 5
        }, ct);

        logger.LogInformation("Created shared scene '{Scene}'", pov.SharedScene);
    }

    private async Task EnsureDiscordSceneAsync(
        HashSet<string> existing, IReadOnlyList<string> kinds, CancellationToken ct)
    {
        if (!existing.Add(DiscordScene))
        {
            logger.LogInformation("Scene '{Scene}' already exists; leaving it untouched", DiscordScene);
            return;
        }

        await obs.CreateSceneAsync(DiscordScene, ct);

        await obs.CreateInputAsync(DiscordScene, "Discord Overlay", PickKind(kinds, "browser_source"),
            new JObject
            {
                ["width"] = CanvasWidth,
                ["height"] = CanvasHeight
            }, ct: ct);

        await obs.CreateInputAsync(DiscordScene, "Discord Audio",
            PickKind(kinds, "wasapi_process_output_capture"), new JObject(), ct: ct);

        logger.LogInformation("Created shared scene '{Scene}'", DiscordScene);
    }

    private async Task EnsurePovSceneAsync(
        PovDescriptor featured, IReadOnlyList<PovDescriptor> all, HashSet<string> existing, CancellationToken ct)
    {
        if (!existing.Add(featured.PovScene))
        {
            logger.LogInformation("Scene '{Scene}' already exists; leaving it untouched", featured.PovScene);
            return;
        }

        await obs.CreateSceneAsync(featured.PovScene, ct);

        var primaryId = await obs.AddSceneItemAsync(featured.PovScene, featured.SharedScene, ct);
        await obs.SetSceneItemTransformAsync(featured.PovScene, primaryId,
            CellTransform(0, 0, PrimaryWidth, PrimaryHeight), ct);

        var cell = 0;
        foreach (var other in all)
        {
            if (other == featured) continue;

            var itemId = await obs.AddSceneItemAsync(featured.PovScene, other.SharedScene, ct);
            var (x, y) = ThumbnailCells[cell++];
            await obs.SetSceneItemTransformAsync(featured.PovScene, itemId,
                CellTransform(x, y, ThumbWidth, ThumbHeight), ct);
        }

        await obs.AddSceneItemAsync(featured.PovScene, DiscordScene, ct);

        logger.LogInformation("Created POV scene '{Scene}'", featured.PovScene);
    }

    private static JObject InsetTransform()
    {
        return CellTransform(BorderPx, BorderPx, CanvasWidth - 2 * BorderPx, CanvasHeight - 2 * BorderPx);
    }

    private static JObject CellTransform(int x, int y, int width, int height)
    {
        return new JObject
        {
            ["positionX"] = x,
            ["positionY"] = y,
            ["alignment"] = 5,
            ["boundsType"] = "OBS_BOUNDS_SCALE_INNER",
            ["boundsAlignment"] = 0,
            ["boundsWidth"] = width,
            ["boundsHeight"] = height
        };
    }

    private static string PickKind(IReadOnlyList<string> kinds, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            var match = kinds.LastOrDefault(k =>
                k == prefix || k.StartsWith(prefix + "_v", StringComparison.Ordinal));
            if (match is not null) return match;
        }

        throw new InvalidOperationException(
            $"No OBS input kind found matching {string.Join(", ", prefixes)}. Available: {string.Join(", ", kinds)}");
    }

    private static long ToObsColor(string hex)
    {
        var value = Convert.ToUInt32(hex.TrimStart('#'), 16);
        var r = (value >> 16) & 0xFF;
        var g = (value >> 8) & 0xFF;
        var b = value & 0xFF;
        return 0xFF000000L | (b << 16) | (g << 8) | r;
    }

    private sealed record PovDescriptor(
        string Label, string DisplayName, string PovScene, string SharedScene, string NameSource, string Color,
        string BrowserSource, bool IsOwn = false);
}
