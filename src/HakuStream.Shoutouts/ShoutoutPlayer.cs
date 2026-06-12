using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HakuStream.Kit.Obs;
using HakuStream.Kit.Twitch.Auth;
using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HakuStream.Shoutouts;

public sealed partial class ShoutoutPlayer(
    ObsClient obs,
    TwitchApiClient twitch,
    TokenManager tokens,
    ShoutoutSettings settings,
    ILogger<ShoutoutPlayer> logger)
{
    private const string GqlClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
    private const string VideoAccessTokenSha = "36b89d2507fce29e5ca551df756d27c1cfe079e2609642b4390aa4c35796eb11";
    private const int SignedUrlAttempts = 3;
    private const int MaxClipCandidates = 5;
    private static readonly TimeSpan SignedUrlRetryDelay = TimeSpan.FromMilliseconds(400);
    private static readonly HttpClient GqlHttp = new();

    public async Task<ShoutoutResolution?> ResolveAsync(string arg, CancellationToken ct)
    {
        twitch.SetAccessToken(tokens.AccessToken);

        var urlMatch = ClipUrlRegex().Match(arg);
        if (urlMatch.Success)
        {
            var slug = urlMatch.Groups["slug"].Value;
            var clipsResponse = await twitch.Api.Helix.Clips.GetClipsAsync(clipIds: [slug]);
            var clip = clipsResponse.Clips.FirstOrDefault();
            if (clip is null)
            {
                logger.LogWarning("Shoutout: clip not found for slug {Slug}", slug);
                return null;
            }

            var broadcaster = await twitch.GetUserByIdAsync(clip.BroadcasterId);
            if (broadcaster is null)
            {
                logger.LogWarning("Shoutout: broadcaster {Id} not found for clip {Slug}", clip.BroadcasterId, slug);
                return null;
            }

            return new ShoutoutResolution(
                broadcaster.Id,
                broadcaster.Login,
                broadcaster.DisplayName,
                clip.Id,
                await GetSignedClipUrlAsync(clip.Id, ct),
                clip.Duration);
        }

        var user = await twitch.GetUserByLoginAsync(arg);
        if (user is null)
        {
            logger.LogWarning("Shoutout: user {User} not found", arg);
            return null;
        }

        var now = DateTime.UtcNow;
        var start = now.AddDays(-settings.ClipsWithinDays);

        var clips = await twitch.Api.Helix.Clips.GetClipsAsync(
            broadcasterId: user.Id,
            startedAt: start,
            endedAt: now,
            first: 100);

        if (clips.Clips.Length == 0)
            clips = await twitch.Api.Helix.Clips.GetClipsAsync(broadcasterId: user.Id, first: 100);

        foreach (var candidate in clips.Clips.OrderBy(_ => Random.Shared.Next()).Take(MaxClipCandidates))
        {
            var signedUrl = await GetSignedClipUrlAsync(candidate.Id, ct);
            if (!string.IsNullOrEmpty(signedUrl))
            {
                return new ShoutoutResolution(
                    user.Id,
                    user.Login,
                    user.DisplayName,
                    candidate.Id,
                    signedUrl,
                    candidate.Duration);
            }

            logger.LogWarning("Shoutout: could not sign clip {Slug} for {User}; trying another", candidate.Id, arg);
        }

        return new ShoutoutResolution(user.Id, user.Login, user.DisplayName, null, null, 0);
    }

    public async Task PlayClipAsync(ShoutoutResolution resolved, CancellationToken ct)
    {
        if (!resolved.HasClip) throw new InvalidOperationException("No clip to play.");

        var duration = TimeSpan.FromSeconds(Math.Max(1, resolved.ClipDurationSeconds));

        logger.LogInformation("Shoutout {User}: clip {Slug} ({Duration}s)",
            resolved.DisplayName, resolved.ClipId, resolved.ClipDurationSeconds);

        try
        {
            await obs.SetSceneItemEnabledAsync(settings.Scene, settings.MediaSource, false, ct);
            await obs.SetSceneItemEnabledAsync(settings.Scene, settings.TextSource, false, ct);
            await obs.SetMediaSourceFileAsync(settings.MediaSource, resolved.ClipMp4Url!, ct);
            await obs.SetTextAsync(settings.TextSource, resolved.DisplayName, ct);
            await Task.Delay(500, ct);
            await obs.SetSceneItemEnabledAsync(settings.Scene, settings.MediaSource, true, ct);
            await obs.SetSceneItemEnabledAsync(settings.Scene, settings.TextSource, true, ct);

            await Task.Delay(duration, ct);
        }
        finally
        {
            try
            {
                await obs.SetSceneItemEnabledAsync(settings.Scene, settings.MediaSource, false, ct);
                await obs.SetSceneItemEnabledAsync(settings.Scene, settings.TextSource, false, ct);
                await obs.SetMediaSourceFileAsync(settings.MediaSource, string.Empty, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up shoutout sources");
            }
        }
    }

    public static bool TryExtractClipUrl(string text, out string url)
    {
        if (!string.IsNullOrEmpty(text))
        {
            var match = ClipUrlRegex().Match(text);
            if (match.Success)
            {
                url = match.Value;
                return true;
            }
        }

        url = string.Empty;
        return false;
    }

    private async Task<string?> GetSignedClipUrlAsync(string slug, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= SignedUrlAttempts; attempt++)
        {
            var url = await TrySignClipUrlAsync(slug, attempt, ct);
            if (!string.IsNullOrEmpty(url)) return url;

            if (attempt < SignedUrlAttempts) await Task.Delay(SignedUrlRetryDelay, ct);
        }

        return null;
    }

    private async Task<string?> TrySignClipUrlAsync(string slug, int attempt, CancellationToken ct)
    {
        var payload = new
        {
            operationName = "VideoAccessToken_Clip",
            variables = new { slug },
            extensions = new { persistedQuery = new { version = 1, sha256Hash = VideoAccessTokenSha } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("Client-ID", GqlClientId);

        try
        {
            using var response = await GqlHttp.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Twitch GQL VideoAccessToken_Clip failed for slug {Slug} (attempt {Attempt}/{Attempts}): {Status} {Body}",
                    slug, attempt, SignedUrlAttempts, response.StatusCode, Truncate(body));
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("clip", out var clip) ||
                clip.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning(
                    "Twitch GQL response missing clip data for slug {Slug} (attempt {Attempt}/{Attempts}): {Body}",
                    slug, attempt, SignedUrlAttempts, Truncate(body));
                return null;
            }

            var qualities = clip.GetProperty("videoQualities");
            if (qualities.GetArrayLength() == 0) return null;

            var index = qualities.GetArrayLength() > 2 ? 2 : 0;
            var sourceUrl = qualities[index].GetProperty("sourceURL").GetString();
            var pat = clip.GetProperty("playbackAccessToken");
            var signature = pat.GetProperty("signature").GetString();
            var token = pat.GetProperty("value").GetString();

            if (string.IsNullOrEmpty(sourceUrl) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(token))
                return null;

            return $"{sourceUrl}?sig={signature}&token={Uri.EscapeDataString(token)}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch signed clip URL for slug {Slug} (attempt {Attempt}/{Attempts})",
                slug, attempt, SignedUrlAttempts);
            return null;
        }
    }

    private static string Truncate(string value)
    {
        return value.Length <= 500 ? value : value[..500];
    }

    [GeneratedRegex(@"(?:https?:\/\/)?(?:www\.)?(?:clips\.twitch\.tv\/|twitch\.tv\/[^\/]+\/clip\/)(?<slug>[^?\s\/]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClipUrlRegex();
}
