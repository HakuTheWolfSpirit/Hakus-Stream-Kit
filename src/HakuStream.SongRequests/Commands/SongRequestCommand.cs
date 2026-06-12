using HakuStream.Kit.Twitch;

namespace HakuStream.SongRequests.Commands;

[Command("sr")]
public sealed class SongRequestCommand(SongRequestService songs) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Length == 0)
        {
            await ctx.ReplyAsync("Usage: !sr <youtube-url or search terms>");
            return;
        }

        var query = string.Join(' ', ctx.Args);
        var bypassRules = ctx.Permission >= PermissionLevel.Moderator;
        var result = await songs.RequestAsync(query, ctx.User, bypassRules, ct);

        if (!result.Success)
        {
            await ctx.ReplyAsync(result.Error!);
            return;
        }

        var note = result.IsMusic ? "" : " (YouTube doesn't list this one as music.)";
        await ctx.ReplyAsync($"Song '{result.Song!.Title}' added to queue at position {result.Position}.{note}");
    }
}
