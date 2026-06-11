using HakuStream.Kit.Twitch.Auth;
using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.Logging;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;

namespace HakuStream.Kit.Twitch;

public sealed class RewardManager(
    TwitchApiClient apiClient,
    TokenManager tokenManager,
    TwitchSettings settings,
    ILogger<RewardManager> logger)
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private string? _broadcasterId;

    public async Task<string> GetBroadcasterIdAsync(CancellationToken ct = default)
    {
        if (_broadcasterId is not null) return _broadcasterId;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_broadcasterId is not null) return _broadcasterId;

            apiClient.SetAccessToken(tokenManager.AccessToken);
            var channel = settings.Channel.TrimStart('#');
            var user = await apiClient.GetUserByLoginAsync(channel)
                       ?? throw new InvalidOperationException(
                           $"Could not resolve broadcaster id for channel '{channel}'");

            _broadcasterId = user.Id;
            return _broadcasterId;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<CustomReward> EnsureRewardAsync(RedeemAttribute reward, CancellationToken ct = default)
    {
        var broadcasterId = await GetBroadcasterIdAsync(ct);
        apiClient.SetAccessToken(tokenManager.AccessToken);

        var existing = await apiClient.Api.Helix.ChannelPoints.GetCustomRewardAsync(
            broadcasterId, rewardIds: null, onlyManageableRewards: false);

        var match = existing?.Data?.FirstOrDefault(r =>
            r.Title.Equals(reward.Title, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            logger.LogInformation("Found existing reward '{Title}' (id={RewardId}, cost={Cost})",
                reward.Title, match.Id, match.Cost);
            return match;
        }

        var created = await apiClient.Api.Helix.ChannelPoints.CreateCustomRewardsAsync(
            broadcasterId, BuildCreateRequest(reward));

        var newReward = created.Data[0];
        logger.LogInformation("Created reward '{Title}' (id={RewardId}, cost={Cost})",
            reward.Title, newReward.Id, newReward.Cost);

        return newReward;
    }

    private static CreateCustomRewardsRequest BuildCreateRequest(RedeemAttribute reward)
    {
        var request = new CreateCustomRewardsRequest
        {
            Title = reward.Title,
            Cost = reward.Cost,
            IsEnabled = true,
            IsUserInputRequired = reward.RequiresUserInput,
            ShouldRedemptionsSkipRequestQueue = reward.SkipRequestQueue
        };

        if (!string.IsNullOrEmpty(reward.Description)) request.Prompt = reward.Description;
        if (!string.IsNullOrEmpty(reward.BackgroundColor)) request.BackgroundColor = reward.BackgroundColor;

        if (reward.MaxPerUserPerStream > 0)
        {
            request.IsMaxPerUserPerStreamEnabled = true;
            request.MaxPerUserPerStream = reward.MaxPerUserPerStream;
        }

        if (reward.GlobalCooldownSeconds > 0)
        {
            request.IsGlobalCooldownEnabled = true;
            request.GlobalCooldownSeconds = reward.GlobalCooldownSeconds;
        }

        return request;
    }

    public async Task SetRewardEnabledAsync(string rewardId, bool enabled, CancellationToken ct = default)
    {
        var broadcasterId = await GetBroadcasterIdAsync(ct);
        apiClient.SetAccessToken(tokenManager.AccessToken);

        await apiClient.Api.Helix.ChannelPoints.UpdateCustomRewardAsync(
            broadcasterId, rewardId, new UpdateCustomRewardRequest { IsEnabled = enabled });

        logger.LogDebug("Set reward {RewardId} enabled={Enabled}", rewardId, enabled);
    }

    public async Task UpdateCostAsync(string rewardId, int newCost, CancellationToken ct = default)
    {
        var broadcasterId = await GetBroadcasterIdAsync(ct);
        apiClient.SetAccessToken(tokenManager.AccessToken);

        await apiClient.Api.Helix.ChannelPoints.UpdateCustomRewardAsync(
            broadcasterId, rewardId, new UpdateCustomRewardRequest { Cost = newCost });

        logger.LogDebug("Updated reward {RewardId} cost to {Cost}", rewardId, newCost);
    }

    public async Task UpdateTitleAsync(
        string rewardId, string newTitle, string? backgroundColor = null, CancellationToken ct = default)
    {
        var broadcasterId = await GetBroadcasterIdAsync(ct);
        apiClient.SetAccessToken(tokenManager.AccessToken);

        var request = new UpdateCustomRewardRequest { Title = newTitle };
        if (!string.IsNullOrEmpty(backgroundColor)) request.BackgroundColor = backgroundColor;

        await apiClient.Api.Helix.ChannelPoints.UpdateCustomRewardAsync(broadcasterId, rewardId, request);

        logger.LogDebug("Updated reward {RewardId} title to '{Title}'", rewardId, newTitle);
    }

    public Task FulfillRedemptionAsync(string rewardId, string redemptionId, CancellationToken ct = default)
    {
        return SetRedemptionStatusAsync(rewardId, redemptionId, CustomRewardRedemptionStatus.FULFILLED, ct);
    }

    public Task CancelRedemptionAsync(string rewardId, string redemptionId, CancellationToken ct = default)
    {
        return SetRedemptionStatusAsync(rewardId, redemptionId, CustomRewardRedemptionStatus.CANCELED, ct);
    }

    private async Task SetRedemptionStatusAsync(
        string rewardId, string redemptionId, CustomRewardRedemptionStatus status, CancellationToken ct)
    {
        var broadcasterId = await GetBroadcasterIdAsync(ct);
        apiClient.SetAccessToken(tokenManager.AccessToken);

        await apiClient.Api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(
            broadcasterId, rewardId, [redemptionId],
            new UpdateCustomRewardRedemptionStatusRequest { Status = status });

        logger.LogDebug("Set redemption {RedemptionId} on reward {RewardId} to {Status}",
            redemptionId, rewardId, status);
    }
}
