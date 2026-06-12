using HakuStream.Kit.Twitch;

namespace HakuStream.SongRequests.Commands;

[Command("volume")]
[RequirePermission(Level = PermissionLevel.Broadcaster)]
public sealed class VolumeCommand(SongPlayer player) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Arg(0) is not { } arg || !int.TryParse(arg, out var volume))
        {
            await ctx.ReplyAsync("Usage: !volume <0-100>");
            return;
        }

        if (volume is < 0 or > 100)
        {
            await ctx.ReplyAsync("Volume must be between 0 and 100.");
            return;
        }

        player.SetVolume(volume);
    }
}
