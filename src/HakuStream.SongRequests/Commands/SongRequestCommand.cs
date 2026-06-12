using HakuStream.Kit.Twitch;

namespace HakuStream.SongRequests.Commands;

[Command("sr")]
public sealed class SongRequestCommand(SongRequestService songs) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Arg(0) is not { } url)
        {
            await ctx.ReplyAsync("Usage: !sr <youtube-url>");
            return;
        }

        var bypassRules = ctx.Permission >= PermissionLevel.Moderator;
        var result = await songs.RequestAsync(url, ctx.User, bypassRules, ct);

        await ctx.ReplyAsync(result.Success
            ? $"Song '{result.Song!.Title}' added to queue at position {result.Position}."
            : result.Error!);
    }
}
