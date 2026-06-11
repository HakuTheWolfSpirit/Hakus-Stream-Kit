using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HakuStream.Kit.Twitch.Auth;

public sealed class TokenRefreshService(
    TwitchOAuthClient oauthClient,
    TokenManager tokenManager,
    TwitchApiClient apiClient,
    TwitchSettings settings,
    ILogger<TokenRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await DetermineDelayAsync(stoppingToken);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await RefreshAsync(stoppingToken);
        }
    }

    private async Task<TimeSpan> DetermineDelayAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tokenManager.AccessToken)) return IdleDelay;

        var validation = await oauthClient.ValidateTokenAsync(tokenManager.AccessToken, cancellationToken);
        if (validation is null) return RetryDelay;

        var delay = TimeSpan.FromSeconds(validation.ExpiresIn) - RefreshBuffer;
        return delay < RetryDelay ? RetryDelay : delay;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tokenManager.RefreshToken)) return;

        var response = await oauthClient.RefreshTokenAsync(tokenManager.RefreshToken, settings.ClientId,
            settings.ClientSecret, cancellationToken);
        if (response is null)
        {
            logger.LogWarning("Proactive token refresh failed; will retry");
            return;
        }

        tokenManager.Save(response.AccessToken, response.RefreshToken);
        apiClient.SetAccessToken(response.AccessToken);
        logger.LogInformation("Twitch access token refreshed proactively");
    }
}
