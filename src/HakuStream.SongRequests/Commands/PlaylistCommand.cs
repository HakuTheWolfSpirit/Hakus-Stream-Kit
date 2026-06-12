using HakuStream.Kit.Twitch;

namespace HakuStream.SongRequests.Commands;

[Command("playlist")]
[RequirePermission(Level = PermissionLevel.Moderator)]
public sealed class PlaylistCommand(SongPlayerService playerService, SongPlayer player) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        if (player.Current is not { } current)
        {
            await ctx.ReplyAsync("Nothing is playing right now.");
            return;
        }

        await ctx.ReplyAsync(playerService.AddCurrentSongToBackup()
            ? $"Added '{current.Title}' to the backup playlist."
            : $"'{current.Title}' is already on the backup playlist.");
    }
}
