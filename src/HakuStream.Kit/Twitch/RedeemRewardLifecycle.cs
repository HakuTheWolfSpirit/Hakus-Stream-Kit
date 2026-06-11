using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HakuStream.Kit.Twitch;

public sealed class RedeemRewardLifecycle(
    RedeemRegistry registry,
    RewardManager rewards,
    ILogger<RedeemRewardLifecycle> logger) : IHostedService
{
    private readonly Dictionary<string, string> _rewardIdsByTitle = new(StringComparer.OrdinalIgnoreCase);

    public bool IsManaged(string rewardId)
    {
        return _rewardIdsByTitle.ContainsValue(rewardId);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var redeem in registry.All)
        {
            if (redeem.Reward.ExternallyManaged) continue;

            try
            {
                var reward = await rewards.EnsureRewardAsync(redeem.Reward, cancellationToken);
                _rewardIdsByTitle[redeem.Reward.Title] = reward.Id;
                await rewards.SetRewardEnabledAsync(reward.Id, true, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to ensure channel point reward '{Title}'", redeem.Reward.Title);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var (title, rewardId) in _rewardIdsByTitle)
        {
            try
            {
                await rewards.SetRewardEnabledAsync(rewardId, false, cancellationToken);
                logger.LogInformation("Disabled channel point reward '{Title}'", title);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to disable channel point reward '{Title}'", title);
            }
        }
    }
}
