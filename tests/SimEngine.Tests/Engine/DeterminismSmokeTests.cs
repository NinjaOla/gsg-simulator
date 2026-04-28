using SimEngine;
using SimEngine.Events;
using SimEngine.Systems;
using SimEngine.Time;
using Xunit;

namespace SimEngine.Tests.Engine;

/// <summary>
/// Phase 0 "done" gate. Two engines with identical options and systems must
/// produce identical observable results — tick number, time, per-system PRNG
/// draws, and event log — regardless of whether batches run in parallel.
/// </summary>
public sealed class DeterminismSmokeTests
{
    private static readonly DateTimeOffset Start =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TwoEngines_SameSeedAndSystems_ProduceIdenticalTraces()
    {
        var traceA = Run(parallel: true, insertionShuffle: false);
        var traceB = Run(parallel: true, insertionShuffle: false);
        Assert.Equal(traceA, traceB);
    }

    [Fact]
    public void ParallelVsSequential_ProducesIdenticalTraces()
    {
        var parallel = Run(parallel: true, insertionShuffle: false);
        var sequential = Run(parallel: false, insertionShuffle: false);
        Assert.Equal(parallel, sequential);
    }

    [Fact]
    public void ShuffledInsertionOrder_ProducesIdenticalTraces()
    {
        var canonical = Run(parallel: true, insertionShuffle: false);
        var shuffled = Run(parallel: true, insertionShuffle: true);
        Assert.Equal(canonical, shuffled);
    }

    private static IReadOnlyList<string> Run(bool parallel, bool insertionShuffle)
    {
        var trace = new List<string>();
        var lockObj = new object();

        void Log(string line)
        {
            lock (lockObj)
            {
                trace.Add(line);
            }
        }

        var alpha = new RngSystem("alpha", order: 0, log: Log);
        var beta = new RngSystem("beta", order: 1, log: Log);
        var gamma = new RngSystem("gamma", order: 2, log: Log);

        var systems = insertionShuffle
            ? new ISimulationSystem[] { gamma, alpha, beta }
            : new ISimulationSystem[] { alpha, beta, gamma };

        var options = new SimulationEngineOptions
        {
            StartDate = Start,
            Seed = 0xC0FFEE,
            DefaultTickDelta = TimeSpan.FromDays(1),
            EnableParallelBatches = parallel,
        };

        var engine = new SimulationEngine(options, systems);
        using var _ = engine.Events.Subscribe<TickMarker>(e => Log($"event:{e.Tick}:{e.System}"));

        engine.StepMany(100);

        // Append engine-level observables last, so any drift shows up clearly.
        var finalTrace = new List<string>(trace.Count + 2)
        {
            $"ticks={engine.TickNumber}",
            $"time={engine.Time.Current:O}",
        };

        // Sort the per-system draws by (tick, system) so that parallel
        // execution order within a batch doesn't affect the comparison —
        // what we assert is "same set of draws, same sequence per system".
        var sorted = trace
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        finalTrace.AddRange(sorted);
        return finalTrace;
    }

    private sealed record TickMarker(long Tick, string System) : ISimulationEvent;

    private sealed class RngSystem : ISimulationSystem
    {
        private readonly Action<string> _log;

        public RngSystem(string name, int order, Action<string> log)
        {
            Name = name;
            Key = name;
            Order = order;
            _log = log;
        }

        public string Name { get; }

        public string Key { get; }

        public TickCadence Cadence => TickCadence.EveryTick;

        public int Order { get; }

        public IReadOnlyCollection<StateKey> Reads => Array.Empty<StateKey>();

        public IReadOnlyCollection<StateKey> Writes => Array.Empty<StateKey>();

        public void Execute(in SimulationContext ctx)
        {
            var draw = ctx.Random.NextUInt64();
            _log($"draw:{ctx.TickNumber:D4}:{Name}:{draw:X16}");
            ctx.Events.Publish(new TickMarker(ctx.TickNumber, Name));
        }
    }
}
