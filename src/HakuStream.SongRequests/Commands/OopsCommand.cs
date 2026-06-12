using HakuStream.Kit.Twitch;

namespace HakuStream.SongRequests.Commands;

[Command("oops")]
[Command("wrongsong")]
public sealed class OopsCommand(SongQueue queue, YouTubeClient youtube) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        QueuedSong? removed;

        if (ctx.Arg(0) is { } arg)
        {
            var videoId = youtube.ExtractVideoId(arg);
            if (videoId is null)
            {
                await ctx.ReplyAsync("Invalid URL.");
                return;
            }

            removed = queue.RemoveByVideoId(ctx.User, videoId);
        }
        else
        {
            removed = queue.RemoveLastByUser(ctx.User);
        }

        await ctx.ReplyAsync(removed is null
            ? "You have no songs to remove."
            : $"Removed '{removed.Title}' from the queue.");
    }
}
