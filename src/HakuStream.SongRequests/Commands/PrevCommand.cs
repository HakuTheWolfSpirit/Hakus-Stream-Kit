using HakuStream.Kit.Twitch;

namespace HakuStream.SongRequests.Commands;

[Command("prev")]
[RequirePermission(Level = PermissionLevel.Broadcaster)]
public sealed class PrevCommand(SongPlayer player, SongQueue queue) : IChatCommand
{
    public Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        var interrupted = player.GoBack();
        if (interrupted is not null) queue.EnqueueFront(interrupted);

        return Task.CompletedTask;
    }
}
