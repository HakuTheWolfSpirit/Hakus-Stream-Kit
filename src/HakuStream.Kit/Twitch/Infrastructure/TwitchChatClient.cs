using HakuStream.Kit.Twitch.Auth;
using Microsoft.Extensions.Logging;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Events;
using TwitchLib.Communication.Models;
using OnConnectedEventArgs = TwitchLib.Client.Events.OnConnectedEventArgs;

namespace HakuStream.Kit.Twitch.Infrastructure;

public sealed class TwitchChatClient
{
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(1);
    private readonly TwitchClient _client;
    private readonly TaskCompletionSource _connectedTcs = new();

    private readonly ILogger<TwitchChatClient> _logger;
    private readonly TokenManager _tokenManager;
    private int _reconnecting;

    private CancellationToken _shutdownToken = CancellationToken.None;
    private volatile bool _stopping;
    private string _username = string.Empty;

    public TwitchChatClient(ILogger<TwitchChatClient> logger, TokenManager tokenManager)
    {
        _logger = logger;
        _tokenManager = tokenManager;
        var clientOptions = new ClientOptions(new ReconnectionPolicy(3000, 30000));
        _client = new TwitchClient(new WebSocketClient(clientOptions));
    }

    public event Func<OnMessageReceivedArgs, Task>? OnMessageReceived;

    public async Task ConnectAsync(string username, string accessToken, string channel,
        CancellationToken cancellationToken = default)
    {
        _shutdownToken = cancellationToken;
        _username = username;

        _client.Initialize(new ConnectionCredentials(username, accessToken), channel);

        _client.OnConnected += OnConnectedHandler;
        _client.OnJoinedChannel += OnJoinedChannelHandler;
        _client.OnMessageReceived += OnMessageReceivedHandler;
        _client.OnError += OnErrorHandler;
        _client.OnDisconnected += OnDisconnectedHandler;

        await _client.ConnectAsync();
    }

    public Task WaitForConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _connectedTcs.Task.WaitAsync(cancellationToken);
    }

    public TwitchClient GetUnderlyingClient()
    {
        return _client;
    }

    private Task OnConnectedHandler(object? sender, OnConnectedEventArgs e)
    {
        _logger.LogInformation("Connected to Twitch IRC");
        return Task.CompletedTask;
    }

    private Task OnJoinedChannelHandler(object? sender, OnJoinedChannelArgs e)
    {
        _logger.LogInformation("Joined channel: {Channel}", e.Channel);
        _connectedTcs.TrySetResult();
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedHandler(object? sender, OnMessageReceivedArgs e)
    {
        if (OnMessageReceived is not null) await OnMessageReceived(e);
    }

    private Task OnErrorHandler(object? sender, OnErrorEventArgs e)
    {
        _logger.LogError(e.Exception, "Twitch chat error");
        return Task.CompletedTask;
    }

    private Task OnDisconnectedHandler(object? sender, OnDisconnectedArgs e)
    {
        if (_stopping || _shutdownToken.IsCancellationRequested)
        {
            _logger.LogInformation("Twitch IRC disconnected");
            return Task.CompletedTask;
        }

        if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0) return Task.CompletedTask;

        _logger.LogWarning("Twitch IRC disconnected unexpectedly; starting reconnect");
        _ = Task.Run(ReconnectLoopAsync);
        return Task.CompletedTask;
    }

    private async Task ReconnectLoopAsync()
    {
        try
        {
            var attempt = 0;
            var delay = InitialReconnectDelay;

            while (!_stopping && !_shutdownToken.IsCancellationRequested && !_client.IsConnected)
            {
                attempt++;
                try
                {
                    await Task.Delay(delay, _shutdownToken);
                    _logger.LogInformation("Twitch IRC reconnect attempt {Attempt}", attempt);
                    _client.SetConnectionCredentials(new ConnectionCredentials(_username, _tokenManager.AccessToken));
                    await _client.ReconnectAsync();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Twitch IRC reconnect attempt {Attempt} failed", attempt);
                }

                delay = TimeSpan.FromSeconds(Math.Min(MaxReconnectDelay.TotalSeconds, delay.TotalSeconds * 2));
            }

            if (_client.IsConnected)
                _logger.LogInformation("Twitch IRC reconnected after {Attempt} attempt(s)", attempt);
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    public async Task DisconnectAsync()
    {
        _stopping = true;
        await _client.DisconnectAsync();
    }
}
