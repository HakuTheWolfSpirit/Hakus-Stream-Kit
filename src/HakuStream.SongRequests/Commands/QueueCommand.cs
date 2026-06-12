using HakuStream.Kit.Twitch;

namespace HakuStream.SongRequests.Commands;

[Command("q")]
[Command("queue")]
public sealed class QueueCommand(SongQueue queue, SongPlayer player) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        var current = player.Current;
        var nowPlaying = current is null ? "" : $"Now playing: {current.Title} ({current.RequestedBy}). ";

        if (queue.IsEmpty)
        {
            await ctx.SendAsync($"{nowPlaying}The queue is empty.");
            return;
        }

        var next = queue.PeekNext(5);
        var songs = next.Select((s, i) => $"{i + 1}. {s.Title} ({s.RequestedBy})");
        await ctx.SendAsync($"{nowPlaying}Queue ({queue.Count} songs): {string.Join(" | ", songs)}");
    }
}
