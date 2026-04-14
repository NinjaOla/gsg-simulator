using System.Collections.Concurrent;

namespace SimEngine.Events;

/// <summary>
/// Event bus that queues publications during a tick and flushes them on the
/// main thread at tick boundary, in insertion order.
///
/// Why deferred: systems run in parallel batches. A synchronous bus would let
/// a subscriber observe an event mid-tick from a worker thread — a read that
/// was never declared in the subscribing system's read set, breaking the
/// determinism contract.
///
/// Re-entrant publishes (a handler calling <see cref="Publish{TEvent}"/>
/// while <see cref="Flush"/> is running) are queued into the <em>next</em>
/// flush. This is what guarantees <see cref="Flush"/> terminates.
/// </summary>
public sealed class DeferredEventBus : IEventBus
{
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly object _subscribersLock = new();
    private readonly Dictionary<Type, List<Delegate>> _subscribers = new();
    private volatile bool _isFlushing;

    public void Publish<TEvent>(TEvent simulationEvent) where TEvent : ISimulationEvent
    {
        // Snapshot subscribers into the queued closure so that unsubscribes
        // after publish-time still deliver this event (FIFO semantics) and
        // subscribes after publish-time do not.
        Delegate[] snapshot;
        lock (_subscribersLock)
        {
            if (!_subscribers.TryGetValue(typeof(TEvent), out var list) || list.Count == 0)
            {
                return;
            }

            snapshot = list.ToArray();
        }

        _pending.Enqueue(() =>
        {
            foreach (var handler in snapshot)
            {
                ((Action<TEvent>)handler)(simulationEvent);
            }
        });
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : ISimulationEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_subscribersLock)
        {
            if (!_subscribers.TryGetValue(typeof(TEvent), out var list))
            {
                list = new List<Delegate>();
                _subscribers[typeof(TEvent)] = list;
            }

            list.Add(handler);
        }

        return new Subscription(this, typeof(TEvent), handler);
    }

    /// <summary>
    /// Delivers all queued events on the calling thread in insertion order.
    /// Must be called on the simulation main thread. Re-entrant publishes
    /// are deferred to the next flush.
    /// </summary>
    internal void Flush()
    {
        if (_isFlushing)
        {
            throw new InvalidOperationException("DeferredEventBus.Flush is not re-entrant.");
        }

        _isFlushing = true;
        try
        {
            // Snapshot the current queue into a fixed-size batch. New publishes
            // (from handlers) go into _pending for the next flush.
            var batch = new List<Action>();
            while (_pending.TryDequeue(out var action))
            {
                batch.Add(action);
            }

            foreach (var action in batch)
            {
                action();
            }
        }
        finally
        {
            _isFlushing = false;
        }
    }

    /// <summary>Engine-internal: discard pending events without delivering them.</summary>
    internal void DiscardPending()
    {
        while (_pending.TryDequeue(out _))
        {
        }
    }

    internal int PendingCount => _pending.Count;

    private void Unsubscribe(Type eventType, Delegate handler)
    {
        lock (_subscribersLock)
        {
            if (_subscribers.TryGetValue(eventType, out var list))
            {
                list.Remove(handler);
                if (list.Count == 0)
                {
                    _subscribers.Remove(eventType);
                }
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly DeferredEventBus _bus;
        private readonly Type _eventType;
        private readonly Delegate _handler;
        private bool _disposed;

        public Subscription(DeferredEventBus bus, Type eventType, Delegate handler)
        {
            _bus = bus;
            _eventType = eventType;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _bus.Unsubscribe(_eventType, _handler);
        }
    }
}
