using HakuStream.Kit.Events;
using HakuStream.Kit.Obs;
using HakuStream.Kit.Storage;
using HakuStream.Kit.Twitch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HakuStream.Archipelago;

public sealed class ApPovEntry
{
    public string Name { get; set; } = string.Empty;
    public string RewardId { get; set; } = string.Empty;
    public string Scene { get; set; } = string.Empty;
    public string SharedScene { get; set; } = string.Empty;
    public string BrowserSource { get; set; } = string.Empty;
}

public sealed class ApState
{
    public List<ApPovEntry> Entries { get; set; } = [];
}

public sealed class ArchipelagoPovService : IHostedService
{
    private readonly IEventBus _eventBus;
    private readonly object _lock = new();
    private readonly ILogger<ArchipelagoPovService> _logger;
    private readonly ObsClient _obs;
    private readonly RewardManager _rewards;
    private readonly ArchipelagoSettings _settings;
    private readonly ApState _state;
    private readonly JsonStore<ApState> _store;
    private IDisposable? _subscription;

    public ArchipelagoPovService(
        ObsClient obs,
        RewardManager rewards,
        IEventBus eventBus,
        ArchipelagoSettings settings,
        ILogger<ArchipelagoPovService> logger)
    {
        _obs = obs;
        _rewards = rewards;
        _eventBus = eventBus;
        _settings = settings;
        _logger = logger;
        _store = new JsonStore<ApState>("archipelago", logger);
        _state = _store.Load();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = _eventBus.Subscribe<ChannelPointRedemptionEvent>(OnRedemptionAsync);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        await DisableRewardsAsync(cancellationToken);
    }

    public async Task SetupAsync(string[] playerNames, CancellationToken ct)
    {
        const int maxNames = ArchipelagoSettings.MaxPovCount - 1;
        if (playerNames.Length == 0 || playerNames.Length > maxNames)
            throw new ArgumentException($"Expected 1 to {maxNames} player names.");

        var desired = new List<(string Name, string Scene, string SharedScene, string BrowserSource, string Color)>
        {
            (_settings.OwnName, _settings.OwnScene, _settings.OwnSharedScene, string.Empty, _settings.OwnColor)
        };

        for (var i = 0; i < playerNames.Length; i++)
        {
            var slot = _settings.SlotFor(i + 2);
            await _obs.SetTextAsync(slot.NameSource, playerNames[i], ct);
            desired.Add((playerNames[i], slot.PovScene, slot.SharedScene, slot.BrowserSource, slot.Color));
        }

        var entries = new List<ApPovEntry>();
        List<ApPovEntry> previous;
        lock (_lock)
        {
            previous = [.. _state.Entries];
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var (name, scene, sharedScene, browserSource, color) = desired[i];
            var rewardId = await EnsurePovRewardAsync(
                $"POV: {name}", color, i < previous.Count ? previous[i].RewardId : null, ct);
            entries.Add(new ApPovEntry
            {
                Name = name,
                RewardId = rewardId,
                Scene = scene,
                SharedScene = sharedScene,
                BrowserSource = browserSource
            });
        }

        foreach (var stale in previous.Skip(desired.Count))
            await TryDisableAsync(stale, ct);

        lock (_lock)
        {
            _state.Entries = entries;
            _store.Save(_state);
        }

        _logger.LogInformation("Archipelago POV setup complete: {Names}",
            string.Join(", ", desired.Select(d => d.Name)));
    }

    public async Task DisableRewardsAsync(CancellationToken ct)
    {
        List<ApPovEntry> entries;
        lock (_lock)
        {
            entries = [.. _state.Entries];
        }

        foreach (var entry in entries)
            await TryDisableAsync(entry, ct);
    }

    private async Task<string> EnsurePovRewardAsync(
        string title, string color, string? existingRewardId, CancellationToken ct)
    {
        if (existingRewardId is not null)
        {
            try
            {
                await _rewards.UpdateTitleAsync(existingRewardId, title, color, ct);
                await _rewards.SetRewardEnabledAsync(existingRewardId, true, ct);
                return existingRewardId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not reuse POV reward {RewardId}; creating a new one", existingRewardId);
            }
        }

        var reward = await _rewards.EnsureRewardAsync(new RedeemAttribute(title)
        {
            Cost = 1,
            Description = "Switch the stream to this runner's POV.",
            BackgroundColor = color,
            GlobalCooldownSeconds = 30,
            SkipRequestQueue = false
        }, ct);

        await _rewards.SetRewardEnabledAsync(reward.Id, true, ct);
        return reward.Id;
    }

    private async Task TryDisableAsync(ApPovEntry entry, CancellationToken ct)
    {
        try
        {
            await _rewards.SetRewardEnabledAsync(entry.RewardId, false, ct);
            _logger.LogInformation("Disabled POV reward '{Name}'", entry.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to disable POV reward '{Name}' ({RewardId})", entry.Name, entry.RewardId);
        }
    }

    private async Task OnRedemptionAsync(ChannelPointRedemptionEvent e, CancellationToken ct)
    {
        List<ApPovEntry> entries;
        lock (_lock)
        {
            entries = [.. _state.Entries];
        }

        var entry = entries.FirstOrDefault(x => x.RewardId == e.RewardId);
        if (entry is null) return;

        try
        {
            await _obs.SetCurrentProgramSceneAsync(entry.Scene, ct);
            await _rewards.FulfillRedemptionAsync(e.RewardId, e.RedemptionId, ct);
            _logger.LogInformation("{User} switched POV to {Name}", e.User, entry.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POV switch to {Name} failed; refunding {User}", entry.Name, e.User);
            await _rewards.CancelRedemptionAsync(e.RewardId, e.RedemptionId, ct);
            return;
        }

        await ApplyAudioRoutingAsync(entry, entries, ct);
    }

    private async Task ApplyAudioRoutingAsync(
        ApPovEntry featured, IReadOnlyList<ApPovEntry> entries, CancellationToken ct)
    {
        try
        {
            var featuredUsesBrowser = featured.BrowserSource.Length > 0 &&
                                      await _obs.GetSceneItemEnabledAsync(
                                          featured.SharedScene, featured.BrowserSource, ct);

            foreach (var entry in entries.Where(x => x.BrowserSource.Length > 0))
                await _obs.SetInputMutedAsync(entry.BrowserSource, entry != featured || !featuredUsesBrowser, ct);

            var shareOwn = _settings.AlwaysShareOwnAudio || !featuredUsesBrowser;
            await _obs.SetInputMutedAsync(_settings.OwnAudioSource, !shareOwn, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio routing for POV '{Name}' failed", featured.Name);
        }
    }
}
