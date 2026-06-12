using HakuStream.Kit.Twitch;

namespace HakuStream.SongRequests.Commands;

[Command("skip")]
[RequirePermission(Level = PermissionLevel.Moderator)]
public sealed class SkipCommand(SongPlayer player) : IChatCommand
{
    public Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        if (player.Current is not null) player.Skip();

        return Task.CompletedTask;
    }
}
