using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HakuStream.Kit.Twitch.Auth;

public sealed class TwitchAuthOrchestrator(
    TwitchOAuthClient oauthClient,
    TokenManager tokenManager,
    string clientId,
    string clientSecret,
    ILogger<TwitchAuthOrchestrator> logger,
    IReadOnlyList<string>? extraScopes = null)
{
    private const int CallbackPort = 3000;
    private const string RedirectUri = "http://localhost:3000/callback";

    private static readonly string[] BaseScopes =
    [
        "chat:read",
        "chat:edit",
        "moderator:manage:shoutouts",
        "channel:read:redemptions",
        "channel:manage:redemptions",
        "channel:manage:vips",
        "channel:manage:raids"
    ];

    private readonly string[] _scopes =
        [.. BaseScopes.Concat(extraScopes ?? []).Distinct(StringComparer.OrdinalIgnoreCase)];

    public async Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (tokenManager.TryLoad())
        {
            var validation = await oauthClient.ValidateTokenAsync(tokenManager.AccessToken, cancellationToken);
            if (validation is not null)
            {
                if (HasAllRequiredScopes(validation.Scopes))
                {
                    tokenManager.UserId = validation.UserId;
                    logger.LogInformation("Existing token is valid with required scopes (user {Login})",
                        validation.Login);
                    return true;
                }

                logger.LogWarning(
                    "Existing token is missing required scopes (have: {Have}; need: {Need}); re-authorizing",
                    string.Join(",", validation.Scopes),
                    string.Join(",", _scopes));
            }
            else if (!string.IsNullOrEmpty(tokenManager.RefreshToken) && await TryRefreshTokenAsync(cancellationToken))
            {
                var refreshed = await oauthClient.ValidateTokenAsync(tokenManager.AccessToken, cancellationToken);
                if (refreshed is not null && HasAllRequiredScopes(refreshed.Scopes))
                {
                    tokenManager.UserId = refreshed.UserId;
                    logger.LogInformation("Token refreshed successfully (user {Login})", refreshed.Login);
                    return true;
                }

                logger.LogWarning("Refreshed token is missing required scopes; re-authorizing");
            }
        }

        return await AuthorizeAsync(cancellationToken);
    }

    private bool HasAllRequiredScopes(IReadOnlyList<string> grantedScopes)
    {
        return _scopes.All(required => grantedScopes.Contains(required, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<bool> TryRefreshTokenAsync(CancellationToken cancellationToken)
    {
        var tokenResponse =
            await oauthClient.RefreshTokenAsync(tokenManager.RefreshToken, clientId, clientSecret, cancellationToken);
        if (tokenResponse is null) return false;

        tokenManager.Save(tokenResponse.AccessToken, tokenResponse.RefreshToken);
        return true;
    }

    private async Task<bool> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        var authUrl = oauthClient.BuildAuthorizationUrl(clientId, RedirectUri, _scopes, state);

        logger.LogInformation(
            "Twitch authorization required. Copy this URL into your browser:{NewLine}{Url}",
            Environment.NewLine,
            authUrl);

        var result =
            await oauthClient.WaitForAuthorizationCallbackAsync(CallbackPort, state, TimeSpan.FromMinutes(5),
                cancellationToken);
        if (result.Code is null)
        {
            logger.LogError("Failed to get authorization code: {Error}", result.Error);
            return false;
        }

        var tokenResponse =
            await oauthClient.ExchangeCodeForTokenAsync(result.Code, clientId, clientSecret, RedirectUri,
                cancellationToken);
        if (tokenResponse is null) return false;

        tokenManager.Save(tokenResponse.AccessToken, tokenResponse.RefreshToken);

        var validation = await oauthClient.ValidateTokenAsync(tokenResponse.AccessToken, cancellationToken);
        if (validation is null)
        {
            logger.LogError("Newly obtained access token failed validation");
            return false;
        }

        tokenManager.UserId = validation.UserId;
        logger.LogInformation("Successfully obtained access token for user {Login}", validation.Login);
        return true;
    }
}
