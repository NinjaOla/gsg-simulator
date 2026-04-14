namespace SimEngine.Systems;

/// <summary>
/// Build-time helper for collecting systems before constructing a
/// <see cref="SimulationEngine"/>. Fluent; not thread-safe; not used once
/// the engine is running (systems are frozen after construction).
/// </summary>
public sealed class SystemRegistry
{
    private readonly List<ISimulationSystem> _systems = new();

    public SystemRegistry Add(ISimulationSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
        return this;
    }

    public IReadOnlyList<ISimulationSystem> Systems => _systems;
}
