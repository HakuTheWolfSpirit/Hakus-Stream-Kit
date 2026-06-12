using HakuStream.Kit.Events;
using Microsoft.Extensions.Hosting;

namespace HakuStream.Shoutouts;

public sealed class LastClipWatcher(IEventBus eventBus, LastClipTracker tracker) : IHostedService
{
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = eventBus.Subscribe<ChatMessageEvent>((e, _) =>
        {
            tracker.TryRecord(e.Message);
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }
}
