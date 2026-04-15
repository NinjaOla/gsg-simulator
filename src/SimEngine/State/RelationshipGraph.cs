using SimEngine.Ids;

namespace SimEngine.State;

/// <summary>
/// Labeled directed multigraph of entity relationships. Stores are mirrored
/// (outbound and inbound) so <see cref="GetInbound"/> is O(log n) rather than
/// a full scan. Outer storage is <see cref="SortedDictionary{TKey,TValue}"/>
/// so enumeration of keys across the graph is deterministic; inner storage
/// is <see cref="SortedSet{T}"/> so enumeration of targets under a given
/// (from, label) is also deterministic.
/// </summary>
public sealed class RelationshipGraph
{
    private readonly SortedDictionary<(EntityId From, RelationshipLabel Label), SortedSet<EntityId>> _outbound = new(EdgeKeyComparer.Instance);
    private readonly SortedDictionary<(EntityId To, RelationshipLabel Label), SortedSet<EntityId>> _inbound = new(EdgeKeyComparer.Instance);
    private int _count;

    public int Count => _count;

    public void Add(EntityId from, RelationshipLabel label, EntityId to)
    {
        if (GetOrCreate(_outbound, (from, label)).Add(to))
        {
            GetOrCreate(_inbound, (to, label)).Add(from);
            _count++;
        }
    }

    public bool Remove(EntityId from, RelationshipLabel label, EntityId to)
    {
        if (!_outbound.TryGetValue((from, label), out var targets) || !targets.Remove(to))
        {
            return false;
        }

        if (targets.Count == 0)
        {
            _outbound.Remove((from, label));
        }

        if (_inbound.TryGetValue((to, label), out var sources))
        {
            sources.Remove(from);
            if (sources.Count == 0)
            {
                _inbound.Remove((to, label));
            }
        }

        _count--;
        return true;
    }

    public bool Contains(EntityId from, RelationshipLabel label, EntityId to)
    {
        return _outbound.TryGetValue((from, label), out var targets) && targets.Contains(to);
    }

    /// <summary>
    /// All entities that <paramref name="from"/> points to under
    /// <paramref name="label"/>, ascending by <see cref="EntityId"/>.
    /// Empty if none.
    /// </summary>
    public IEnumerable<EntityId> GetOutbound(EntityId from, RelationshipLabel label)
    {
        return _outbound.TryGetValue((from, label), out var targets)
            ? targets
            : Enumerable.Empty<EntityId>();
    }

    /// <summary>
    /// All entities that point to <paramref name="to"/> under
    /// <paramref name="label"/>, ascending by <see cref="EntityId"/>.
    /// Empty if none.
    /// </summary>
    public IEnumerable<EntityId> GetInbound(EntityId to, RelationshipLabel label)
    {
        return _inbound.TryGetValue((to, label), out var sources)
            ? sources
            : Enumerable.Empty<EntityId>();
    }

    /// <summary>
    /// Every (label, target) pair emitted by <paramref name="from"/>, in
    /// ascending order by (label, target).
    /// </summary>
    public IEnumerable<(RelationshipLabel Label, EntityId To)> GetAllOutbound(EntityId from)
    {
        // Keys are sorted by (EntityId, Label). Skip until we reach `from`,
        // then emit until we're past it.
        foreach (var kvp in _outbound)
        {
            if (kvp.Key.From < from)
            {
                continue;
            }

            if (kvp.Key.From > from)
            {
                yield break;
            }

            foreach (var to in kvp.Value)
            {
                yield return (kvp.Key.Label, to);
            }
        }
    }

    public void Clear()
    {
        _outbound.Clear();
        _inbound.Clear();
        _count = 0;
    }

    private static SortedSet<EntityId> GetOrCreate(
        SortedDictionary<(EntityId, RelationshipLabel), SortedSet<EntityId>> dict,
        (EntityId, RelationshipLabel) key)
    {
        if (!dict.TryGetValue(key, out var set))
        {
            set = new SortedSet<EntityId>();
            dict[key] = set;
        }

        return set;
    }

    private sealed class EdgeKeyComparer : IComparer<(EntityId Entity, RelationshipLabel Label)>
    {
        public static readonly EdgeKeyComparer Instance = new();

        public int Compare(
            (EntityId Entity, RelationshipLabel Label) x,
            (EntityId Entity, RelationshipLabel Label) y)
        {
            var entityCmp = x.Entity.CompareTo(y.Entity);
            if (entityCmp != 0)
            {
                return entityCmp;
            }

            return ((int)x.Label).CompareTo((int)y.Label);
        }
    }
}
