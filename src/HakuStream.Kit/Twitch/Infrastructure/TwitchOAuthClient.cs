using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HakuStream.Kit.Twitch.Infrastructure;

public sealed class TwitchOAuthClient(ILogger<TwitchOAuthClient> logger)
{
    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string ValidateEndpoint = "https://id.twitch.tv/oauth2/validate";
    private const string AuthorizeEndpoint = "https://id.twitch.tv/oauth2/authorize";
    private readonly HttpClient _httpClient = new();

    public async Task<TokenValidationResult?> ValidateTokenAsync(string accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ValidateEndpoint);
            request.Headers.Add("Authorization", $"OAuth {accessToken}");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<TokenValidationResult>(cancellationToken)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<TokenResponse?> RefreshTokenAsync(string refreshToken, string clientId, string clientSecret,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            });

            var response = await _httpClient.PostAsync(TokenEndpoint, content, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh token");
            return null;
        }
    }

    public async Task<TokenResponse?> ExchangeCodeForTokenAsync(string code, string clientId, string clientSecret,
        string redirectUri, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            });

            var response = await _httpClient.PostAsync(TokenEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Token exchange failed: {Error}",
                    await response.Content.ReadAsStringAsync(cancellationToken));
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to exchange code for token");
            return null;
        }
    }

    public string BuildAuthorizationUrl(string clientId, string redirectUri, string[] scopes, string state)
    {
        return $"{AuthorizeEndpoint}" +
               $"?client_id={clientId}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&response_type=code" +
               $"&scope={string.Join("+", scopes)}" +
               $"&state={state}";
    }

    public void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    public async Task<AuthorizationCallbackResult> WaitForAuthorizationCallbackAsync(int port, string expectedState,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        logger.LogInformation("Waiting for authorization callback on port {Port}...", port);

        try
        {
            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(timeout, cancellationToken));

            if (completedTask != contextTask)
            {
                logger.LogError("Authorization timed out");
                return new AuthorizationCallbackResult(null, "Timeout");
            }

            var context = await contextTask;
            var query = context.Request.QueryString;
            var code = query["code"];
            var state = query["state"];

            string responseHtml;
            if (state != expectedState)
            {
                responseHtml = "<html><body><h1>Authorization Failed</h1><p>State mismatch.</p></body></html>";
                await SendResponseAsync(context.Response, responseHtml, 400);
                return new AuthorizationCallbackResult(null, "State mismatch");
            }

            if (string.IsNullOrEmpty(code))
            {
                var error = query["error"] ?? "Unknown error";
                responseHtml = $"<html><body><h1>Authorization Failed</h1><p>{error}</p></body></html>";
                await SendResponseAsync(context.Response, responseHtml, 400);
                return new AuthorizationCallbackResult(null, error);
            }

            responseHtml =
                "<html><body><h1>Authorization Successful!</h1><p>You can close this window.</p></body></html>";
            await SendResponseAsync(context.Response, responseHtml, 200);
            return new AuthorizationCallbackResult(code, null);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task SendResponseAsync(HttpListenerResponse response, string html, int statusCode)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/html";
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }
}

public record TokenResponse(
    [property: JsonPropertyName("access_token")]
    string AccessToken,
    [property: JsonPropertyName("refresh_token")]
    string RefreshToken);

public record TokenValidationResult(
    [property: JsonPropertyName("client_id")]
    string ClientId,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("user_id")]
    string UserId,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string> Scopes,
    [property: JsonPropertyName("expires_in")]
    int ExpiresIn);

public record AuthorizationCallbackResult(string? Code, string? Error);
