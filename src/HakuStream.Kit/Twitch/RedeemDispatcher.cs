using HakuStream.Kit.Events;
using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HakuStream.Kit.Twitch;

public sealed class RedeemDispatcher(
    IEventBus eventBus,
    IServiceProvider services,
    RedeemRegistry registry,
    RewardManager rewards,
    RedeemRewardLifecycle lifecycle,
    TwitchChatWriter writer,
    ILogger<RedeemDispatcher> logger) : IHostedService
{
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = eventBus.Subscribe<ChannelPointRedemptionEvent>(DispatchAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }

    private async Task DispatchAsync(ChannelPointRedemptionEvent e, CancellationToken ct)
    {
        if (!registry.TryGet(e.RewardTitle, out var info))
        {
            if (lifecycle.IsManaged(e.RewardId))
                logger.LogWarning(
                    "Redemption '{Title}' has no handler but the reward is bot-managed — was it renamed?",
                    e.RewardTitle);
            return;
        }

        var ctx = new RedeemContext
        {
            RedemptionId = e.RedemptionId,
            RewardId = e.RewardId,
            RewardTitle = e.RewardTitle,
            Cost = e.Cost,
            User = e.User,
            UserId = e.UserId,
            UserInput = e.UserInput,
            Send = message => writer.SendMessageAsync(message),
            Fulfill = info.Reward.SkipRequestQueue
                ? _ => LogAutoFulfilled("Fulfill", e.RewardTitle)
                : token => rewards.FulfillRedemptionAsync(e.RewardId, e.RedemptionId, token),
            Cancel = info.Reward.SkipRequestQueue
                ? _ => LogAutoFulfilled("Cancel", e.RewardTitle)
                : token => rewards.CancelRedemptionAsync(e.RewardId, e.RedemptionId, token)
        };

        await using var scope = services.CreateAsyncScope();
        var redeem = (IChannelPointsRedeem)scope.ServiceProvider.GetRequiredService(info.HandlerType);

        try
        {
            await redeem.RunAsync(ctx, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redeem '{Title}' by {User} threw", e.RewardTitle, e.User);
        }
    }

    private Task LogAutoFulfilled(string action, string title)
    {
        logger.LogDebug("{Action} skipped for '{Title}': SkipRequestQueue rewards are auto-fulfilled by Twitch",
            action, title);
        return Task.CompletedTask;
    }
}
