using SimEngine;
using SimEngine.Systems;
using SimEngine.Time;

namespace SimEngine.Tests.TestSupport;

/// <summary>
/// Test system that records each invocation. Configurable cadence, order,
/// reads, writes, and an optional <see cref="Action"/> hook.
/// </summary>
internal sealed class RecordingSystem : ISimulationSystem
{
    private readonly List<SimulationContextSnapshot> _invocations = new();
    private readonly object _lock = new();
    private readonly Action<SimulationContextSnapshot>? _onExecute;

    public RecordingSystem(
        string name,
        TickCadence cadence = TickCadence.EveryTick,
        int order = 0,
        StateKey[]? reads = null,
        StateKey[]? writes = null,
        Action<SimulationContextSnapshot>? onExecute = null)
    {
        Name = name;
        Key = name;
        Cadence = cadence;
        Order = order;
        Reads = reads ?? Array.Empty<StateKey>();
        Writes = writes ?? Array.Empty<StateKey>();
        _onExecute = onExecute;
    }

    public string Name { get; }

    public string Key { get; }

    public TickCadence Cadence { get; }

    public int Order { get; }

    public IReadOnlyCollection<StateKey> Reads { get; }

    public IReadOnlyCollection<StateKey> Writes { get; }

    public IReadOnlyList<SimulationContextSnapshot> Invocations
    {
        get
        {
            lock (_lock)
            {
                return _invocations.ToArray();
            }
        }
    }

    public void Execute(in SimulationContext ctx)
    {
        var snapshot = new SimulationContextSnapshot(
            ctx.TickNumber,
            ctx.TickStart,
            ctx.TickEnd,
            Environment.CurrentManagedThreadId);

        lock (_lock)
        {
            _invocations.Add(snapshot);
        }

        _onExecute?.Invoke(snapshot);
    }
}

internal readonly record struct SimulationContextSnapshot(
    long TickNumber,
    DateTimeOffset TickStart,
    DateTimeOffset TickEnd,
    int ThreadId);


