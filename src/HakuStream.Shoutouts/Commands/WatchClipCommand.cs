using HakuStream.Kit.Twitch;
using Microsoft.Extensions.Logging;

namespace HakuStream.Shoutouts.Commands;

[Command("watchclip")]
[RequirePermission(Level = PermissionLevel.Moderator)]
public sealed class WatchClipCommand(
    LastClipTracker tracker,
    ShoutoutPlayer player,
    ILogger<WatchClipCommand> logger) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        var url = tracker.LastClipUrl;
        if (string.IsNullOrEmpty(url))
        {
            logger.LogDebug("!watchclip called but no clip has been posted yet");
            return;
        }

        var resolved = await player.ResolveAsync(url, ct);
        if (resolved is null || !resolved.HasClip)
        {
            logger.LogWarning("Tracked clip URL {Url} no longer resolves", url);
            return;
        }

        await player.PlayClipAsync(resolved, ct);
    }
}
