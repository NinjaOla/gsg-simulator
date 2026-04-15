using SimEngine.Ids;

namespace SimEngine.State.Pathfinding;

/// <summary>
/// Per-edge cost function for A*/Dijkstra. Must return a non-negative
/// integer for any adjacent (from, to) pair the pathfinder visits.
/// </summary>
public delegate int ProvinceEdgeCost(ProvinceId from, ProvinceId to);

/// <summary>
/// Optional heuristic estimating the remaining cost from
/// <paramref name="node"/> to <paramref name="goal"/>. Must be admissible
/// (never overestimate) and consistent (monotone) for A* to stay optimal.
/// Must return a non-negative integer.
/// </summary>
public delegate int ProvinceHeuristic(ProvinceId node, ProvinceId goal);
