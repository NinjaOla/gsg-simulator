namespace SimEngine.State;

/// <summary>
/// Root of the simulation's mutable world state. A single instance is owned
/// by <see cref="SimulationEngine"/> and surfaced to systems via
/// <see cref="SimulationContext.State"/>.
///
/// Phase 1a deliberately keeps this small: an entity/component store, a
/// labeled relationship graph, and a province adjacency graph. Phase 2
/// (save/load) will serialize this type whole.
/// </summary>
public sealed class SimulationState
{
    public EntityStore Entities { get; }

    public RelationshipGraph Relationships { get; }

    /// <summary>
    /// Arbitrary key/value metadata owned by the game layer. The engine does not
    /// interpret these values.
    /// </summary>
    public IDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Province adjacency graph. Defaults to <see cref="AdjacencyGraph.Empty"/>
    /// so an engine with no world loaded still has a valid graph to query.
    /// Replaced wholesale by world loaders — never mutated in place.
    /// </summary>
    public AdjacencyGraph Adjacency { get; internal set; }

    public SimulationState()
        : this(AdjacencyGraph.Empty)
    {
    }

    public SimulationState(AdjacencyGraph adjacency)
    {
        ArgumentNullException.ThrowIfNull(adjacency);

        Entities = new EntityStore();
        Relationships = new RelationshipGraph();
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        Adjacency = adjacency;
    }
}
