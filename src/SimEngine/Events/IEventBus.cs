namespace SimEngine.Events;

/// <summary>
/// Publish/subscribe event bus for cross-system communication.
/// Implementations decide when delivery happens relative to publication —
/// the default <see cref="DeferredEventBus"/> defers delivery to the end of
/// the current tick to preserve read/write declaration honesty under
/// parallel batch execution.
/// </summary>
public interface IEventBus
{
    /// <summary>Publish an event. Thread-safe.</summary>
    void Publish<TEvent>(TEvent simulationEvent) where TEvent : ISimulationEvent;

    /// <summary>
    /// Subscribe to events of a specific type. Disposing the returned token
    /// unsubscribes the handler.
    /// </summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : ISimulationEvent;
}
