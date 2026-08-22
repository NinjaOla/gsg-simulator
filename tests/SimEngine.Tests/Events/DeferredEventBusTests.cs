using SimEngine.Events;
using Xunit;

namespace SimEngine.Tests.Events;

public sealed class DeferredEventBusTests
{
    private sealed record PingEvent(int Value) : ISimulationEvent;

    private sealed record PongEvent(string Label) : ISimulationEvent;

    private static void Flush(DeferredEventBus bus)
    {
        typeof(DeferredEventBus)
            .GetMethod("Flush", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(bus, Array.Empty<object>());
    }

    [Fact]
    public void Publish_WithoutFlush_DoesNotDeliver()
    {
        var bus = new DeferredEventBus();
        var received = 0;
        using var _ = bus.Subscribe<PingEvent>(_ => received++);

        bus.Publish(new PingEvent(1));
        bus.Publish(new PingEvent(2));

        Assert.Equal(0, received);
    }

    [Fact]
    public void Flush_DeliversAllPendingInInsertionOrder()
    {
        var bus = new DeferredEventBus();
        var order = new List<int>();
        using var _ = bus.Subscribe<PingEvent>(e => order.Add(e.Value));

        bus.Publish(new PingEvent(1));
        bus.Publish(new PingEvent(2));
        bus.Publish(new PingEvent(3));
        Flush(bus);

        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public void Subscribe_IsTypeTargeted()
    {
        var bus = new DeferredEventBus();
        var pingCount = 0;
        var pongCount = 0;
        using var _1 = bus.Subscribe<PingEvent>(_ => pingCount++);
        using var _2 = bus.Subscribe<PongEvent>(_ => pongCount++);

        bus.Publish(new PingEvent(1));
        bus.Publish(new PongEvent("hi"));
        bus.Publish(new PingEvent(2));
        Flush(bus);

        Assert.Equal(2, pingCount);
        Assert.Equal(1, pongCount);
    }

    [Fact]
    public void Dispose_Unsubscribes()
    {
        var bus = new DeferredEventBus();
        var received = 0;
        var sub = bus.Subscribe<PingEvent>(_ => received++);

        bus.Publish(new PingEvent(1));
        Flush(bus);
        Assert.Equal(1, received);

        sub.Dispose();
        bus.Publish(new PingEvent(2));
        Flush(bus);
        Assert.Equal(1, received);
    }

    [Fact]
    public void ConcurrentPublish_DeliversAllEvents()
    {
        var bus = new DeferredEventBus();
        var received = new List<int>();
        using var _ = bus.Subscribe<PingEvent>(e =>
        {
            lock (received)
            {
                received.Add(e.Value);
            }
        });

        const int perThread = 500;
        const int threads = 8;
        Parallel.For(0, threads, t =>
        {
            for (var i = 0; i < perThread; i++)
            {
                bus.Publish(new PingEvent(t * perThread + i));
            }
        });

        Flush(bus);
        Assert.Equal(threads * perThread, received.Count);
        Assert.Equal(threads * perThread, received.Distinct().Count());
    }

    [Fact]
    public void ReEntrantPublish_QueuesIntoNextFlush()
    {
        var bus = new DeferredEventBus();
        var firstFlushSeen = new List<int>();
        var secondFlushSeen = new List<int>();
        var phase = 0;
        using var _ = bus.Subscribe<PingEvent>(e =>
        {
            if (phase == 0)
            {
                firstFlushSeen.Add(e.Value);
                if (e.Value == 1)
                {
                    // Re-entrant publish during flush.
                    bus.Publish(new PingEvent(99));
                }
            }
            else
            {
                secondFlushSeen.Add(e.Value);
            }
        });

        bus.Publish(new PingEvent(1));
        Flush(bus);
        phase = 1;
        Flush(bus);

        Assert.Equal(new[] { 1 }, firstFlushSeen);
        Assert.Equal(new[] { 99 }, secondFlushSeen);
    }
}


