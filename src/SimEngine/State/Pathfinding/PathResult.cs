using SimEngine.Ids;

namespace SimEngine.State.Pathfinding;

/// <summary>
/// Outcome of an <see cref="AStarPathfinder.FindPath"/> call.
/// When <see cref="Found"/> is false, <see cref="Nodes"/> is empty and
/// <see cref="TotalCost"/> is 0. When true, <see cref="Nodes"/> contains
/// the province sequence from start to goal inclusive.
/// </summary>
public readonly record struct PathResult(
    bool Found,
    IReadOnlyList<ProvinceId> Nodes,
    long TotalCost)
{
    public static PathResult Unreachable { get; } = new(false, Array.Empty<ProvinceId>(), 0);
}
