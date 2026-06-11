using TwitchLib.Client;

namespace HakuStream.Kit.Twitch.Infrastructure;

public sealed class TwitchChatWriter
{
    private readonly TaskCompletionSource _connectedTcs = new();
    private string _channel = string.Empty;
    private TwitchClient? _client;

    public void Initialize(TwitchClient client, string channel)
    {
        _client = client;
        _channel = channel;
    }

    public void SetConnected()
    {
        _connectedTcs.TrySetResult();
    }

    public async Task SendMessageAsync(string message)
    {
        if (_client is null) return;

        await _connectedTcs.Task;
        await _client.SendMessageAsync(_channel, message);
    }

    public async Task SendReplyAsync(string replyToId, string message)
    {
        if (_client is null) return;

        await _connectedTcs.Task;
        await _client.SendReplyAsync(_channel, replyToId, message);
    }
}
