namespace HakuStream.Kit.Twitch;

public interface IChatCommand
{
    Task RunAsync(CommandContext ctx, CancellationToken ct);
}
