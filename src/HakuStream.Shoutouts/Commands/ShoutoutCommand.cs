using HakuStream.Kit.Twitch;
using HakuStream.Kit.Twitch.Auth;
using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HakuStream.Shoutouts.Commands;

[Command("so")]
[Command("shoutout")]
[RequirePermission(Level = PermissionLevel.Moderator)]
[Cooldown(Scope = CooldownScope.PerCommand, Seconds = 3)]
public sealed class ShoutoutCommand(
    ShoutoutPlayer player,
    ShoutoutSettings settings,
    TwitchApiClient api,
    TokenManager tokens,
    RewardManager rewards,
    ILogger<ShoutoutCommand> logger) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        var target = ctx.Arg(0)?.TrimStart('@');
        if (string.IsNullOrWhiteSpace(target))
        {
            await ctx.ReplyAsync("Usage: !so <username>");
            return;
        }

        var resolved = await player.ResolveAsync(target, ct);
        if (resolved is null)
        {
            await ctx.ReplyAsync($"Couldn't find a user called '{target}'.");
            return;
        }

        var login = resolved.Login ?? resolved.DisplayName.ToLowerInvariant();
        var message = settings.Message
            .Replace("{name}", resolved.DisplayName)
            .Replace("{url}", $"https://twitch.tv/{login}");
        if (!string.IsNullOrWhiteSpace(message)) await ctx.SendAsync(message);

        try
        {
            api.SetAccessToken(tokens.AccessToken);
            var broadcasterId = await rewards.GetBroadcasterIdAsync(ct);
            await api.Api.Helix.Chat.SendShoutoutAsync(broadcasterId, resolved.UserId, tokens.UserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Native Twitch /shoutout failed (cooldown or not live); chat message still sent");
        }

        if (!resolved.HasClip) return;

        await player.PlayClipAsync(resolved, ct);
    }
}
