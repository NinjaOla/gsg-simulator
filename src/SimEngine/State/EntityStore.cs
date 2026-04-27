using SimEngine.Ids;

namespace SimEngine.State;

/// <summary>
/// Holds every entity in the simulation and routes component attach/detach/
/// query operations to the right typed <see cref="ComponentStore{T}"/>.
///
/// Iteration contracts (critical for determinism):
/// <list type="bullet">
///   <item><see cref="Query{T}"/> yields results ascending by
///     <see cref="EntityId"/>.</item>
///   <item><see cref="All"/> yields every live entity ascending by
///     <see cref="EntityId"/>.</item>
///   <item>Cross-type operations (e.g. <see cref="Destroy"/>) walk the
///     internal <see cref="Dictionary{TKey,TValue}"/> of stores via a
///     sorted-by-type-name index to avoid relying on hash iteration order.</item>
/// </list>
///
/// Callers must not <c>.AsParallel()</c> the enumerables returned by
/// <see cref="Query{T}"/> or <see cref="All"/> from within a system — that
/// would defeat the deterministic ordering contract.
/// </summary>
public sealed class EntityStore
{
    private readonly Dictionary<Type, IComponentStore> _componentStoresByType = new();
    private readonly SortedDictionary<string, IComponentStore> _componentStoresByName = new(StringComparer.Ordinal);
    private readonly SortedSet<EntityId> _liveEntities = new();
    private uint _nextId = 1;

    public int Count => _liveEntities.Count;

    /// <summary>All live entities, ascending by <see cref="EntityId"/>.</summary>
    public IEnumerable<EntityId> All => _liveEntities;

    /// <summary>Allocates a new entity with a monotonic <see cref="EntityId"/> starting at 1.</summary>
    public EntityId Create()
    {
        var id = new EntityId(_nextId++);
        _liveEntities.Add(id);
        return id;
    }

    internal EntityId Create(EntityId id)
    {
        if (id == EntityId.None)
        {
            throw new ArgumentException("EntityId.None is not a valid entity id.", nameof(id));
        }

        if (!_liveEntities.Add(id))
        {
            throw new InvalidOperationException($"Entity {id} already exists.");
        }

        if (id.Value >= _nextId)
        {
            _nextId = checked(id.Value + 1);
        }

        return id;
    }

    public bool Exists(EntityId id) => _liveEntities.Contains(id);

    /// <summary>
    /// Removes the entity and any components attached to it. Cascades
    /// through every component store in deterministic type-name order.
    /// </summary>
    /// <returns>true if the entity existed.</returns>
    public bool Destroy(EntityId id)
    {
        if (!_liveEntities.Remove(id))
        {
            return false;
        }

        // Walk the SortedDictionary mirror, not the hash-keyed Dictionary,
        // so the removal order is deterministic across runs.
        foreach (var store in _componentStoresByName.Values)
        {
            store.Remove(id);
        }

        return true;
    }

    public void Attach<T>(EntityId id, T component) where T : struct
    {
        if (!_liveEntities.Contains(id))
        {
            throw new KeyNotFoundException($"Cannot attach component to unknown entity {id}.");
        }

        Store<T>().Set(id, component);
    }

    public bool Detach<T>(EntityId id) where T : struct
    {
        if (!_componentStoresByType.TryGetValue(typeof(T), out var store))
        {
            return false;
        }

        return store.Remove(id);
    }

    public bool TryGet<T>(EntityId id, out T component) where T : struct
    {
        if (!_componentStoresByType.TryGetValue(typeof(T), out var store))
        {
            component = default;
            return false;
        }

        return ((ComponentStore<T>)store).TryGet(id, out component);
    }

    public bool Has<T>(EntityId id) where T : struct
    {
        return _componentStoresByType.TryGetValue(typeof(T), out var store)
               && store.Contains(id);
    }

    /// <summary>
    /// Direct ref into the component store for in-place mutation. Throws if
    /// the entity has no component of type <typeparamref name="T"/>.
    /// </summary>
    public ref T GetRef<T>(EntityId id) where T : struct
    {
        if (!_componentStoresByType.TryGetValue(typeof(T), out var store))
        {
            throw new KeyNotFoundException($"No component store exists for type {typeof(T).Name}.");
        }

        return ref ((ComponentStore<T>)store).GetRef(id);
    }

    /// <summary>
    /// Enumerate every (entity, component) pair for a component type,
    /// ascending by <see cref="EntityId"/>. Returns an empty sequence if
    /// no entity carries the component.
    /// </summary>
    public IEnumerable<(EntityId Id, T Component)> Query<T>() where T : struct
    {
        return _componentStoresByType.TryGetValue(typeof(T), out var store)
            ? ((ComponentStore<T>)store).Enumerate()
            : Enumerable.Empty<(EntityId, T)>();
    }

    public int CountOf<T>() where T : struct
    {
        return _componentStoresByType.TryGetValue(typeof(T), out var store) ? store.Count : 0;
    }

    private ComponentStore<T> Store<T>() where T : struct
    {
        if (_componentStoresByType.TryGetValue(typeof(T), out var existing))
        {
            return (ComponentStore<T>)existing;
        }

        var store = new ComponentStore<T>();
        _componentStoresByType[typeof(T)] = store;
        _componentStoresByName[typeof(T).FullName ?? typeof(T).Name] = store;
        return store;
    }
}
