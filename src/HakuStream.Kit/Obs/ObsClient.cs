using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace HakuStream.Kit.Obs;

public sealed class ObsClient : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly ILogger<ObsClient> _logger;
    private readonly OBSWebsocket _obs = new();
    private readonly ObsSettings _settings;
    private TaskCompletionSource<bool> _connectTcs = new();

    public ObsClient(ObsSettings settings, ILogger<ObsClient> logger)
    {
        _settings = settings;
        _logger = logger;

        _obs.Connected += (_, _) =>
        {
            _logger.LogInformation("OBS WebSocket connected to {Host}:{Port}", _settings.Host, _settings.Port);
            _connectTcs.TrySetResult(true);
        };
        _obs.Disconnected += (_, args) =>
            _logger.LogWarning("OBS WebSocket disconnected: {Reason}", args.DisconnectReason);
    }

    public void Dispose()
    {
        if (_obs.IsConnected) _obs.Disconnect();
        _connectLock.Dispose();
    }

    public async Task<IReadOnlyList<string>> GetSceneNamesAsync(CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        return _obs.GetSceneList().Scenes.Select(s => s.Name).ToList();
    }

    public async Task<IReadOnlyList<string>> GetInputKindsAsync(CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        return _obs.GetInputKindList(false);
    }

    public async Task CreateSceneAsync(string sceneName, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        _obs.CreateScene(sceneName);
    }

    public async Task<int> CreateInputAsync(
        string sceneName, string inputName, string inputKind, JObject inputSettings, bool enabled = true,
        CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        return _obs.CreateInput(sceneName, inputName, inputKind, inputSettings, enabled);
    }

    public async Task<bool> GetSceneItemEnabledAsync(string sceneName, string sourceName, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        var itemId = _obs.GetSceneItemId(sceneName, sourceName, 0);
        return _obs.GetSceneItemEnabled(sceneName, itemId);
    }

    public async Task SetInputMutedAsync(string inputName, bool muted, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        _obs.SetInputMute(inputName, muted);
    }

    public async Task<int> AddSceneItemAsync(string sceneName, string sourceName, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        return _obs.CreateSceneItem(sceneName, sourceName, true);
    }

    public async Task SetSceneItemTransformAsync(
        string sceneName, int sceneItemId, JObject transform, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        _obs.SetSceneItemTransform(sceneName, sceneItemId, transform);
    }

    public async Task SetSceneItemEnabledAsync(
        string sceneName, string sourceName, bool enabled, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        var itemId = _obs.GetSceneItemId(sceneName, sourceName, 0);
        _obs.SetSceneItemEnabled(sceneName, itemId, enabled);
    }

    public async Task SetMediaSourceFileAsync(string sourceName, string filePathOrUrl, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var isLocal = !LooksLikeUrl(filePathOrUrl);
        var settings = new JObject
        {
            ["is_local_file"] = isLocal,
            ["local_file"] = isLocal ? filePathOrUrl : string.Empty,
            ["input"] = isLocal ? string.Empty : filePathOrUrl
        };

        _obs.SetInputSettings(sourceName, settings, true);
    }

    public async Task SetTextAsync(string sourceName, string text, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        _obs.SetInputSettings(sourceName, new JObject { ["text"] = text }, true);
    }

    public async Task SetCurrentProgramSceneAsync(string sceneName, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        _obs.SetCurrentProgramScene(sceneName);
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_obs.IsConnected) return;

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_obs.IsConnected) return;

            _connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _obs.ConnectAsync($"ws://{_settings.Host}:{_settings.Port}", _settings.Password ?? string.Empty);

            var timeout = Task.Delay(ConnectTimeout, ct);
            var completed = await Task.WhenAny(_connectTcs.Task, timeout);
            if (completed == timeout)
                throw new TimeoutException($"Timed out connecting to OBS at {_settings.Host}:{_settings.Port}");

            await _connectTcs.Task;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private static bool LooksLikeUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
