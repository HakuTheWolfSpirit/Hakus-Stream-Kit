using HakuStream.Kit.Events;
using HakuStream.Kit.Twitch.Auth;
using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwitchLib.Api.Core.Enums;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;

namespace HakuStream.Kit.Twitch;

public sealed class TwitchEventSubService(
    EventSubWebsocketClient client,
    TwitchApiClient apiClient,
    TokenManager tokenManager,
    RewardManager rewardManager,
    IEventBus eventBus,
    ILogger<TwitchEventSubService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        client.WebsocketConnected += OnWebsocketConnected;
        client.WebsocketReconnected += OnWebsocketReconnected;
        client.WebsocketDisconnected += OnWebsocketDisconnected;
        client.ErrorOccurred += OnErrorOccurred;
        client.ChannelPointsCustomRewardRedemptionAdd += OnRedemptionAdd;
        client.ChannelRaid += OnChannelRaid;

        var connected = await client.ConnectAsync();
        if (!connected)
        {
            logger.LogError("Failed to connect to Twitch EventSub WebSocket");
            return;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error disconnecting EventSub WebSocket");
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs args)
    {
        logger.LogInformation("EventSub WebSocket connected (sessionId={SessionId}, reconnect={Reconnect})",
            client.SessionId, args.IsRequestedReconnect);

        if (args.IsRequestedReconnect) return;

        try
        {
            await SubscribeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create EventSub subscriptions");
        }
    }

    private Task OnWebsocketReconnected(object? sender, EventArgs args)
    {
        logger.LogInformation("EventSub WebSocket reconnected");
        return Task.CompletedTask;
    }

    private Task OnWebsocketDisconnected(object? sender, EventArgs args)
    {
        logger.LogWarning("EventSub WebSocket disconnected");
        return Task.CompletedTask;
    }

    private Task OnErrorOccurred(object? sender, ErrorOccuredArgs args)
    {
        logger.LogError(args.Exception, "EventSub error: {Message}", args.Message);
        return Task.CompletedTask;
    }

    private async Task SubscribeAsync()
    {
        var broadcasterId = await rewardManager.GetBroadcasterIdAsync();
        apiClient.SetAccessToken(tokenManager.AccessToken);

        await apiClient.Api.Helix.EventSub.CreateEventSubSubscriptionAsync(
            "channel.channel_points_custom_reward_redemption.add",
            "1",
            new Dictionary<string, string> { ["broadcaster_user_id"] = broadcasterId },
            EventSubTransportMethod.Websocket,
            client.SessionId);

        await apiClient.Api.Helix.EventSub.CreateEventSubSubscriptionAsync(
            "channel.raid",
            "1",
            new Dictionary<string, string> { ["from_broadcaster_user_id"] = broadcasterId },
            EventSubTransportMethod.Websocket,
            client.SessionId);

        logger.LogInformation("Subscribed to channel point redemptions and outgoing raids for {BroadcasterId}",
            broadcasterId);
    }

    private async Task OnRedemptionAdd(object? sender, ChannelPointsCustomRewardRedemptionArgs args)
    {
        var data = args.Payload.Event;

        await eventBus.PublishAsync(new ChannelPointRedemptionEvent(
            data.Id,
            data.Reward.Id,
            data.Reward.Title,
            data.Reward.Cost,
            data.UserName,
            data.UserId,
            data.BroadcasterUserLogin,
            data.BroadcasterUserId,
            string.IsNullOrEmpty(data.UserInput) ? null : data.UserInput), CancellationToken.None);
    }

    private async Task OnChannelRaid(object? sender, ChannelRaidArgs args)
    {
        var data = args.Payload.Event;

        logger.LogInformation("Raid completed: {From} -> {To} with {Viewers} viewers",
            data.FromBroadcasterUserLogin, data.ToBroadcasterUserLogin, data.Viewers);

        await eventBus.PublishAsync(new RaidSentEvent(
            data.FromBroadcasterUserId,
            data.FromBroadcasterUserLogin,
            data.FromBroadcasterUserName,
            data.ToBroadcasterUserId,
            data.ToBroadcasterUserLogin,
            data.ToBroadcasterUserName,
            data.Viewers), CancellationToken.None);
    }
}
