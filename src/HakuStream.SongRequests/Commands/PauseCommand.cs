using HakuStream.Kit.Twitch;

namespace HakuStream.SongRequests.Commands;

[Command("pause")]
[RequirePermission(Level = PermissionLevel.Broadcaster)]
public sealed class PauseCommand(SongPlayer player) : IChatCommand
{
    public Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        player.TogglePause();
        return Task.CompletedTask;
    }
}
