using SimEngine.State.Components;

namespace SimEngine.State.Loading;

/// <summary>
/// Hydrates a simulation world from a stream. Phase 1a defines the contract;
/// the concrete GeoJSON implementation lands in Phase 1b once a geometry
/// library decision is made.
/// </summary>
public interface IWorldLoader
{
    WorldLoadResult Load(Stream source);
}

/// <summary>
/// Intermediate province description produced by a loader. Loaders do not
/// allocate entities or attach components themselves — they return seed
/// records, and <see cref="WorldBuilder"/> turns them into state.
/// </summary>
public readonly record struct ProvinceSeed(
    string Name,
    Terrain Terrain,
    int CentroidLatE6,
    int CentroidLonE6);

/// <summary>
/// Output of an <see cref="IWorldLoader"/>. The list index corresponds to
/// the order in which provinces should be added to the world; downstream
/// consumers are expected to feed the seeds into a <see cref="WorldBuilder"/>
/// in this order and then replay the adjacency edges.
/// </summary>
public sealed record WorldLoadResult(
    IReadOnlyList<ProvinceSeed> Provinces,
    AdjacencyGraph Adjacency);
