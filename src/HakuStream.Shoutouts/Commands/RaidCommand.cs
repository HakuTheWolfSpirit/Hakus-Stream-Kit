using HakuStream.Kit.Twitch;
using HakuStream.Kit.Twitch.Auth;
using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HakuStream.Shoutouts.Commands;

[Command("raid")]
[RequirePermission(Level = PermissionLevel.Moderator)]
public sealed class RaidCommand(
    ShoutoutPlayer player,
    TwitchApiClient twitch,
    TokenManager tokens,
    RewardManager rewards,
    ILogger<RaidCommand> logger) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Arg(0) is not { } target) return;

        ShoutoutResolution? resolved;
        try
        {
            resolved = await player.ResolveAsync(target.TrimStart('@'), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not resolve target {Target}; no shoutout, clip, or raid attempted", target);
            return;
        }

        if (resolved is null)
        {
            logger.LogInformation("Target {Target} not found; no shoutout, clip, or raid attempted", target);
            return;
        }

        if (!resolved.HasClip)
        {
            logger.LogInformation("No clip available for {Target}; no shoutout, clip, or raid attempted", target);
            return;
        }

        twitch.SetAccessToken(tokens.AccessToken);
        var broadcasterId = await rewards.GetBroadcasterIdAsync(ct);
        var login = resolved.Login ?? resolved.DisplayName.ToLowerInvariant();

        try
        {
            await twitch.Api.Helix.Chat.SendShoutoutAsync(broadcasterId, resolved.UserId, tokens.UserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Native /shoutout failed (cooldown or missing scope); continuing");
        }

        try
        {
            await player.PlayClipAsync(resolved, ct);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Raid cancelled during clip playback");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Clip playback failed; aborting raid");
            return;
        }

        try
        {
            await twitch.Api.Helix.Raids.StartRaidAsync(broadcasterId, resolved.UserId);
            logger.LogInformation("Raid initiated to {Login} ({UserId})", login, resolved.UserId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start raid to {Login}", login);
        }
    }
}
