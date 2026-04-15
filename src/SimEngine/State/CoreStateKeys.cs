using SimEngine.Systems;

namespace SimEngine.State;

/// <summary>
/// Well-known <see cref="StateKey"/> constants for the engine's core state
/// slices. Systems that mutate the entity store structurally (create/destroy)
/// or edit relationships/adjacency declare these in their Writes; systems
/// that only read them declare them in Reads.
/// </summary>
public static class CoreStateKeys
{
    public static readonly StateKey Entities = new("state/entities");
    public static readonly StateKey Relationships = new("state/relationships");
    public static readonly StateKey Adjacency = new("state/adjacency");
}
