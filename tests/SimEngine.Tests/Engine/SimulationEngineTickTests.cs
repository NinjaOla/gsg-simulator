using SimEngine;
using SimEngine.Events;
using SimEngine.Systems;
using SimEngine.Tests.TestSupport;
using SimEngine.Time;
using Xunit;

namespace SimEngine.Tests.Engine;

public sealed class SimulationEngineTickTests
{
    private static readonly DateTimeOffset Start =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SimulationEngineOptions Options(
        TimeSpan? delta = null,
        bool parallel = true)
        => new()
        {
            StartDate = Start,
            Seed = 42,
            DefaultTickDelta = delta ?? TimeSpan.FromDays(1),
            EnableParallelBatches = parallel,
        };

    [Fact]
    public void EmptyEngine_StepManyAdvancesTime()
    {
        var engine = new SimulationEngine(Options(), Array.Empty<ISimulationSystem>());
        engine.StepMany(10);
        Assert.Equal(10, engine.TickNumber);
        Assert.Equal(Start + TimeSpan.FromDays(10), engine.Time.Current);
    }

    [Fact]
    public void EveryTickSystem_RunsOnEveryStep()
    {
        var sys = new RecordingSystem("every", TickCadence.EveryTick);
        var engine = new SimulationEngine(Options(), new ISimulationSystem[] { sys });
        engine.StepMany(5);
        Assert.Equal(5, sys.Invocations.Count);
    }

    [Fact]
    public void DailySystem_OnHourlyTicks_FiresOnlyOnDayRollover()
    {
        var sys = new RecordingSystem("daily", TickCadence.Daily);
        var engine = new SimulationEngine(
            Options(delta: TimeSpan.FromHours(1)),
            new ISimulationSystem[] { sys });
        engine.StepMany(48); // two days
        Assert.Equal(2, sys.Invocations.Count);
    }

    [Fact]
    public void MonthlySystem_OnDailyTicks_FiresExactly12TimesPerYear()
    {
        var sys = new RecordingSystem("monthly", TickCadence.Monthly);
        var engine = new SimulationEngine(
            Options(delta: TimeSpan.FromDays(1)),
            new ISimulationSystem[] { sys });
        engine.StepMany(365);
        Assert.Equal(12, sys.Invocations.Count);
    }

    [Fact]
    public void BatchOrder_WriterRunsBeforeReader()
    {
        var key = new StateKey("state");
        var order = new List<string>();
        var lockObj = new object();

        void Record(string name)
        {
            lock (lockObj)
            {
                order.Add(name);
            }
        }

        var writer = new RecordingSystem(
            "writer",
            TickCadence.EveryTick,
            order: 0,
            writes: new[] { key },
            onExecute: _ => Record("writer"));
        var reader = new RecordingSystem(
            "reader",
            TickCadence.EveryTick,
            order: 0,
            reads: new[] { key },
            onExecute: _ => Record("reader"));

        var engine = new SimulationEngine(
            Options(),
            new ISimulationSystem[] { reader, writer }); // insertion order inverted
        engine.Step();
        Assert.Equal(new[] { "writer", "reader" }, order);
    }

    [Fact]
    public void ParallelBatch_RunsSystemsConcurrently()
    {
        using var barrier = new Barrier(2);
        var a = new RecordingSystem(
            "A",
            TickCadence.EveryTick,
            onExecute: _ => barrier.SignalAndWait(TimeSpan.FromSeconds(2)));
        var b = new RecordingSystem(
            "B",
            TickCadence.EveryTick,
            onExecute: _ => barrier.SignalAndWait(TimeSpan.FromSeconds(2)));

        var engine = new SimulationEngine(Options(), new ISimulationSystem[] { a, b });
        engine.Step();
        Assert.Single(a.Invocations);
        Assert.Single(b.Invocations);
    }

    [Fact]
    public void Events_PublishedInTick_DeliveredAtTickEnd()
    {
        var engine = new SimulationEngine(Options(), new[] { new PublishingSystem() });
        var received = new List<int>();
        using var _ = engine.Events.Subscribe<TestEvent>(e => received.Add(e.Value));

        // No events yet — subscriber attached post-construction.
        engine.Step();
        Assert.Single(received);
        Assert.Equal(1, received[0]);

        engine.Step();
        Assert.Equal(2, received.Count);
    }

    [Fact]
    public void Cancellation_RollsBackTick()
    {
        var sys = new RecordingSystem(
            "canceller",
            TickCadence.EveryTick,
            onExecute: _ => { });
        var engine = new SimulationEngine(Options(), new ISimulationSystem[] { sys });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => engine.Step(cts.Token));
        Assert.Equal(0, engine.TickNumber);
        Assert.Equal(Start, engine.Time.Current);
    }

    [Fact]
    public void TickContext_HasStartAndEndMatchingWindow()
    {
        SimulationContextSnapshot? observed = null;
        var sys = new RecordingSystem(
            "observer",
            TickCadence.EveryTick,
            onExecute: snap => observed = snap);
        var engine = new SimulationEngine(
            Options(delta: TimeSpan.FromHours(6)),
            new ISimulationSystem[] { sys });
        engine.Step();

        Assert.NotNull(observed);
        Assert.Equal(Start, observed!.Value.TickStart);
        Assert.Equal(Start + TimeSpan.FromHours(6), observed.Value.TickEnd);
        Assert.Equal(1, observed.Value.TickNumber);
    }

    // --- helpers -----------------------------------------------------------

    private sealed record TestEvent(int Value) : ISimulationEvent;

    private sealed class PublishingSystem : ISimulationSystem
    {
        public string Name => "publisher";
        public string Key => "publisher";

        public TickCadence Cadence => TickCadence.EveryTick;

        public int Order => 0;

        public IReadOnlyCollection<StateKey> Reads => Array.Empty<StateKey>();

        public IReadOnlyCollection<StateKey> Writes => Array.Empty<StateKey>();

        public void Execute(in SimulationContext ctx)
        {
            ctx.Events.Publish(new TestEvent((int)ctx.TickNumber));
        }
    }
}
