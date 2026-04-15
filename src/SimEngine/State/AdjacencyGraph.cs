using System.Collections.Frozen;
using System.Collections.Immutable;
using SimEngine.Ids;

namespace SimEngine.State;

/// <summary>
/// Immutable undirected province adjacency graph. Built once at world load
/// and read many times during pathfinding and system queries. Storage is a
/// <see cref="FrozenDictionary{TKey,TValue}"/> mapping each known
/// <see cref="ProvinceId"/> to a sorted <see cref="ImmutableArray{T}"/> of
/// its neighbors; <see cref="FrozenDictionary{TKey,TValue}"/> is optimized
/// for read-after-build workloads.
///
/// Determinism contracts:
/// <list type="bullet">
///   <item>Within a given province, <see cref="NeighborsOf"/> always returns
///     neighbors in ascending <see cref="ProvinceId"/> order.</item>
///   <item><see cref="FrozenDictionary{TKey,TValue}"/> enumeration order is
///     NOT guaranteed. Callers that need to walk every province in the graph
///     must sort by <see cref="ProvinceId"/> themselves.</item>
/// </list>
/// </summary>
public sealed class AdjacencyGraph
{
    private readonly FrozenDictionary<ProvinceId, ImmutableArray<ProvinceId>> _neighbors;

    /// <summary>Singleton empty graph, useful as the default for a fresh <see cref="SimulationState"/>.</summary>
    public static AdjacencyGraph Empty { get; } = new(
        FrozenDictionary<ProvinceId, ImmutableArray<ProvinceId>>.Empty,
        edgeCount: 0);

    private AdjacencyGraph(
        FrozenDictionary<ProvinceId, ImmutableArray<ProvinceId>> neighbors,
        int edgeCount)
    {
        _neighbors = neighbors;
        EdgeCount = edgeCount;
    }

    public int ProvinceCount => _neighbors.Count;

    public int EdgeCount { get; }

    /// <summary>
    /// Neighbors of <paramref name="id"/>, sorted ascending. Returns an empty
    /// array if the province is unknown to this graph.
    /// </summary>
    public ImmutableArray<ProvinceId> NeighborsOf(ProvinceId id)
    {
        return _neighbors.TryGetValue(id, out var neighbors) ? neighbors : ImmutableArray<ProvinceId>.Empty;
    }

    public bool AreAdjacent(ProvinceId a, ProvinceId b)
    {
        if (!_neighbors.TryGetValue(a, out var neighbors))
        {
            return false;
        }

        // Neighbors are sorted by ProvinceId.Value via the builder's comparer.
        return neighbors.BinarySearch(b, ProvinceIdComparer.Instance) >= 0;
    }

    /// <summary>
    /// Incremental builder. Accepts undirected edges, deduplicates, sorts,
    /// and freezes into an <see cref="AdjacencyGraph"/>.
    /// </summary>
    public sealed class Builder
    {
        private readonly Dictionary<ProvinceId, SortedSet<ProvinceId>> _neighbors = new();

        /// <summary>
        /// Registers a province that may have no edges yet (e.g. an isolated
        /// island). Edges added later via <see cref="AddUndirectedEdge"/>
        /// still register both endpoints, so this is only needed for
        /// truly edge-less provinces.
        /// </summary>
        public void AddProvince(ProvinceId id)
        {
            if (id == ProvinceId.None)
            {
                throw new ArgumentException("ProvinceId.None is not a valid province.", nameof(id));
            }

            if (!_neighbors.ContainsKey(id))
            {
                _neighbors[id] = new SortedSet<ProvinceId>();
            }
        }

        public void AddUndirectedEdge(ProvinceId a, ProvinceId b)
        {
            if (a == ProvinceId.None || b == ProvinceId.None)
            {
                throw new ArgumentException("ProvinceId.None is not a valid endpoint.");
            }

            if (a == b)
            {
                throw new ArgumentException("Self-loops are not supported in the adjacency graph.");
            }

            EnsureSet(a).Add(b);
            EnsureSet(b).Add(a);
        }

        public AdjacencyGraph Build()
        {
            var builder = new Dictionary<ProvinceId, ImmutableArray<ProvinceId>>(_neighbors.Count);
            var edges = 0;

            foreach (var (id, targets) in _neighbors)
            {
                // SortedSet enumerates ascending, so the resulting ImmutableArray
                // is already in sorted order.
                builder[id] = ImmutableArray.CreateRange(targets);
                edges += targets.Count;
            }

            return new AdjacencyGraph(builder.ToFrozenDictionary(), edges / 2);
        }

        private SortedSet<ProvinceId> EnsureSet(ProvinceId id)
        {
            if (!_neighbors.TryGetValue(id, out var set))
            {
                set = new SortedSet<ProvinceId>();
                _neighbors[id] = set;
            }

            return set;
        }
    }

    private sealed class ProvinceIdComparer : IComparer<ProvinceId>
    {
        public static readonly ProvinceIdComparer Instance = new();

        public int Compare(ProvinceId x, ProvinceId y) => x.CompareTo(y);
    }
}
