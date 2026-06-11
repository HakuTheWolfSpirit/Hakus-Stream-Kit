using HakuStream.Kit.Events;
using HakuStream.Kit.Twitch.Auth;
using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Events;

namespace HakuStream.Kit.Twitch;

public sealed class TwitchChatService(
    TwitchChatClient chatClient,
    TwitchChatWriter chatWriter,
    TwitchChatMessageParser parser,
    CommandDispatcher dispatcher,
    IEventBus eventBus,
    TwitchSettings settings,
    TokenManager tokenManager,
    ILogger<TwitchChatService> logger) : BackgroundService
{
    private CancellationToken _stoppingToken = CancellationToken.None;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        chatClient.OnMessageReceived += OnMessageReceived;

        await chatClient.ConnectAsync(settings.BotUsername, tokenManager.AccessToken, settings.Channel, stoppingToken);
        chatWriter.Initialize(chatClient.GetUnderlyingClient(), settings.Channel);

        await chatClient.WaitForConnectionAsync(stoppingToken);
        chatWriter.SetConnected();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            logger.LogInformation("TwitchChatService stopping");
        }
    }

    private async Task OnMessageReceived(OnMessageReceivedArgs args)
    {
        try
        {
            var msg = args.ChatMessage;

            await eventBus.PublishAsync(new ChatMessageEvent(
                msg.Id,
                msg.Message,
                msg.DisplayName,
                msg.UserId,
                msg.Channel,
                msg.UserDetail.IsModerator || msg.IsBroadcaster,
                msg.IsBroadcaster), _stoppingToken);

            var command = parser.TryParse(args);
            if (command is not null) await dispatcher.DispatchAsync(command, _stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed handling chat message");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await chatClient.DisconnectAsync().WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Twitch disconnect timed out");
        }

        await base.StopAsync(cancellationToken);
    }
}
