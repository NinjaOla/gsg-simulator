using SimEngine.Events;
using SimEngine.Random;
using SimEngine.State;
using SimEngine.State.Serialization;
using SimEngine.Systems;
using SimEngine.Time;

namespace SimEngine;

/// <summary>
/// The simulation tick loop. Owns time, PRNG, event bus, and the built
/// system dependency graph. Calling <see cref="Step()"/> advances the clock
/// by <see cref="SimulationEngineOptions.DefaultTickDelta"/> and runs every
/// system whose cadence fires during the new window, in dependency order,
/// with systems in the same batch optionally parallelized.
/// </summary>
public sealed class SimulationEngine
{
    private readonly SystemDependencyGraph _graph;
    private readonly SimulationTimeProvider _time;
    private readonly Xoshiro256StarStar _random;
    private readonly DeferredEventBus _events;
    private readonly SimulationState _state;
    private readonly Dictionary<string, Xoshiro256StarStar> _systemRandoms;
    private long _tickNumber;

    public SimulationEngine(SimulationEngineOptions options, IEnumerable<ISimulationSystem> systems)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(systems);

        Options = options;
        _time = new SimulationTimeProvider(options.StartDate);
        _random = new Xoshiro256StarStar(options.Seed);
        _events = new DeferredEventBus();
        _state = options.InitialState ?? new SimulationState();

        var systemList = systems.ToArray();
        _graph = SystemDependencyGraph.Build(systemList);

        // Validate system keys before forking PRNGs.
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var system in systemList)
        {
            if (string.IsNullOrWhiteSpace(system.Key))
            {
                throw new ArgumentException($"System '{system.Name}' has a blank or whitespace Key.", nameof(systems));
            }

            if (!seenKeys.Add(system.Key))
            {
                throw new ArgumentException($"Duplicate system Key '{system.Key}'.", nameof(systems));
            }
        }

        // Pre-fork per-system PRNGs. Keyed by system Key so the stream a
        // system sees is stable across display-name changes.
        _systemRandoms = new Dictionary<string, Xoshiro256StarStar>(StringComparer.Ordinal);
        foreach (var system in systemList)
        {
            var streamId = StableNameHash(system.Key);
            _systemRandoms[system.Key] = (Xoshiro256StarStar)_random.Fork(streamId);
        }
    }

    public SimulationEngineOptions Options { get; }

    public SimulationTimeProvider Time => _time;

    /// <summary>Root PRNG stream. Systems receive forked sub-streams.</summary>
    public IDeterministicRandom Random => _random;

    public IEventBus Events => _events;

    public SimulationState State => _state;

    public long TickNumber => _tickNumber;

    public IReadOnlyList<IReadOnlyList<ISimulationSystem>> Batches => _graph.Batches;

    /// <summary>Saves the full deterministic engine snapshot to a JSON file.</summary>
    public void Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        Save(stream);
    }

    /// <summary>Saves the full deterministic engine snapshot to a JSON stream.</summary>
    public void Save(Stream stream) => SimulationSaveSerializer.Save(this, stream);

    /// <summary>Loads a full deterministic engine snapshot from a JSON file.</summary>
    public static SimulationEngine Load(
        string path,
        IEnumerable<ISimulationSystem> systems,
        IReadOnlyList<IComponentSectionCodec>? componentCodecs = null,
        IReadOnlyList<IStateSectionCodec>? stateSectionCodecs = null,
        IReadOnlyDictionary<string, string>? expectedSaveMetadata = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
        }

        using var stream = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        return Load(stream, systems, componentCodecs, stateSectionCodecs, expectedSaveMetadata);
    }

    /// <summary>Loads a full deterministic engine snapshot from a JSON stream.</summary>
    public static SimulationEngine Load(
        Stream stream,
        IEnumerable<ISimulationSystem> systems,
        IReadOnlyList<IComponentSectionCodec>? componentCodecs = null,
        IReadOnlyList<IStateSectionCodec>? stateSectionCodecs = null,
        IReadOnlyDictionary<string, string>? expectedSaveMetadata = null)
        => SimulationSaveSerializer.Load(stream, systems, componentCodecs, stateSectionCodecs, expectedSaveMetadata);

    public void Step(CancellationToken ct = default) => Step(Options.DefaultTickDelta, ct);

    public void Step(TimeSpan delta, CancellationToken ct = default)
    {
        if (delta <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), delta, "delta must be positive.");
        }

        var previousTime = _time.Current;
        _time.Advance(delta);
        var currentTime = _time.Current;
        _tickNumber++;

        try
        {
            ct.ThrowIfCancellationRequested();

            foreach (var batch in _graph.Batches)
            {
                ct.ThrowIfCancellationRequested();

                var firingSystems = new List<ISimulationSystem>(batch.Count);
                foreach (var system in batch)
                {
                    if (TickScheduler.ShouldFire(system.Cadence, previousTime, currentTime))
                    {
                        firingSystems.Add(system);
                    }
                }

                if (firingSystems.Count == 0)
                {
                    continue;
                }

                if (Options.EnableParallelBatches && firingSystems.Count > 1)
                {
                    var parallelOptions = new ParallelOptions
                    {
                        CancellationToken = ct,
                        MaxDegreeOfParallelism = Options.MaxDegreeOfParallelism ?? Environment.ProcessorCount,
                    };

                    Parallel.ForEach(firingSystems, parallelOptions, system =>
                    {
                        ExecuteSystem(system, previousTime, currentTime, ct);
                    });
                }
                else
                {
                    foreach (var system in firingSystems)
                    {
                        ExecuteSystem(system, previousTime, currentTime, ct);
                    }
                }
            }

            _events.Flush();
            _time.CommitTick(previousTime);
        }
        catch (OperationCanceledException)
        {
            // Rollback: tick is atomic. Time reverts, tick number un-incremented,
            // pending events discarded.
            _time.RollbackTo(previousTime);
            _tickNumber--;
            _events.DiscardPending();
            throw;
        }
    }

    public void StepMany(int ticks, CancellationToken ct = default)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "ticks must be non-negative.");
        }

        for (var i = 0; i < ticks; i++)
        {
            Step(ct);
        }
    }

    private void ExecuteSystem(
        ISimulationSystem system,
        DateTimeOffset tickStart,
        DateTimeOffset tickEnd,
        CancellationToken ct)
    {
        var ctx = new SimulationContext
        {
            Time = _time,
            Random = _systemRandoms[system.Key],
            Events = _events,
            State = _state,
            TickNumber = _tickNumber,
            TickStart = tickStart,
            TickEnd = tickEnd,
            CancellationToken = ct,
        };

        system.Execute(in ctx);
    }

    /// <summary>
    /// FNV-1a 64-bit hash of the system name. Deterministic across runtimes,
    /// which is what matters for seeding sub-streams; it is not a cryptographic
    /// hash and does not need to be.
    /// </summary>
    private static ulong StableNameHash(string name)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offset;
        foreach (var ch in name)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
    }

    internal IReadOnlyCollection<string> SystemKeys => _systemRandoms.Keys;

    internal (ulong s0, ulong s1, ulong s2, ulong s3) SnapshotRootRandomState() => _random.SnapshotState();

    internal IReadOnlyDictionary<string, (ulong s0, ulong s1, ulong s2, ulong s3)> SnapshotSystemRandomStates()
        => _systemRandoms.ToDictionary(entry => entry.Key, entry => entry.Value.SnapshotState(), StringComparer.Ordinal);

    internal void RestoreRuntime(
        DateTimeOffset currentTime,
        DateTimeOffset previousTick,
        long tickNumber,
        RandomSnapshot rootRandom,
        IEnumerable<SystemRandomSnapshot> systemRandoms)
    {
        if (tickNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickNumber), tickNumber, "tickNumber must be non-negative.");
        }

        ArgumentNullException.ThrowIfNull(systemRandoms);

        _time.Restore(currentTime, previousTick);
        _tickNumber = tickNumber;
        _random.RestoreState(rootRandom.S0, rootRandom.S1, rootRandom.S2, rootRandom.S3);

        foreach (var systemRandom in systemRandoms)
        {
            if (!_systemRandoms.TryGetValue(systemRandom.SystemKey, out var rng))
            {
                throw new InvalidOperationException($"System with key '{systemRandom.SystemKey}' is not registered on this engine.");
            }

            rng.RestoreState(
                systemRandom.Random.S0,
                systemRandom.Random.S1,
                systemRandom.Random.S2,
                systemRandom.Random.S3);
        }
    }
}
