namespace SimEngine.Events;

/// <summary>
/// Marker interface for events published through <see cref="IEventBus"/>.
/// Concrete event types are defined by game code; the engine only provides
/// plumbing in Phase 0.
/// </summary>
public interface ISimulationEvent
{
}
