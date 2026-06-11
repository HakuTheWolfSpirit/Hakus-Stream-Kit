using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HakuStream.Kit.Events;

public sealed class EventBus(ILogger<EventBus> logger) : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : BotEvent
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;

        List<object> snapshot;
        lock (handlers)
        {
            snapshot = handlers.ToList();
        }

        var tasks = new List<Task>();
        foreach (var handler in snapshot)
            if (handler is Func<TEvent, CancellationToken, Task> func)
                tasks.Add(InvokeAsync(func, @event, cancellationToken));

        await Task.WhenAll(tasks);
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : BotEvent
    {
        var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => []);
        lock (handlers)
        {
            handlers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        });
    }

    private async Task InvokeAsync<TEvent>(
        Func<TEvent, CancellationToken, Task> handler,
        TEvent @event,
        CancellationToken cancellationToken) where TEvent : BotEvent
    {
        try
        {
            await handler(@event, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling event {EventType}", typeof(TEvent).Name);
        }
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0) onDispose();
        }
    }
}
