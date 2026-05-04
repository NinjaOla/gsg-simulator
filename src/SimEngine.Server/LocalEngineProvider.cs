namespace SimEngine.Server;

/// <summary>
/// Provides access to <see cref="SimulationEngine"/> instances hosted in-process.
/// Used by local clients (e.g., ConsoleHost) to query engine state directly
/// without serializing through grain methods.
/// </summary>
public interface ILocalEngineProvider
{
    /// <summary>Registers an engine instance for the given session ID.</summary>
    void Register(string sessionId, SimulationEngine engine);

    /// <summary>Removes the engine instance for the given session ID.</summary>
    void Unregister(string sessionId);

    /// <summary>Gets the engine instance for the given session ID, or null if not found.</summary>
    SimulationEngine? GetEngine(string sessionId);
}

/// <inheritdoc/>
internal sealed class LocalEngineProvider : ILocalEngineProvider
{
    private readonly Dictionary<string, SimulationEngine> _engines = [];

    public void Register(string sessionId, SimulationEngine engine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(engine);
        _engines[sessionId] = engine;
    }

    public void Unregister(string sessionId)
    {
        _engines.Remove(sessionId);
    }

    public SimulationEngine? GetEngine(string sessionId)
    {
        _engines.TryGetValue(sessionId, out var engine);
        return engine;
    }
}
