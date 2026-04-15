using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SimEngine.Ids;

namespace SimEngine.State;

/// <summary>
/// Non-generic handle for a <see cref="ComponentStore{T}"/>. Used so
/// <see cref="EntityStore"/> can hold a heterogeneous collection of stores
/// keyed by <see cref="Type"/> while still supporting "destroy this entity
/// from every store" without reflection on generics.
/// </summary>
internal interface IComponentStore
{
    /// <summary>Removes the entity from this store if present.</summary>
    /// <returns>true if the entity had a component here.</returns>
    bool Remove(EntityId id);

    bool Contains(EntityId id);

    int Count { get; }
}

/// <summary>
/// Typed per-component storage. Primary storage is a <see cref="Dictionary{TKey,TValue}"/>
/// for O(1) lookups and genuine by-ref access (via
/// <see cref="CollectionsMarshal.GetValueRefOrNullRef{TKey,TValue}"/>);
/// a mirrored <see cref="SortedSet{T}"/> of keys supplies deterministic
/// ascending-by-<see cref="EntityId"/> iteration without a per-query sort.
/// Components are values (structs), so plain lookups return copies.
/// </summary>
internal sealed class ComponentStore<T> : IComponentStore where T : struct
{
    private readonly Dictionary<EntityId, T> _items = new();
    private readonly SortedSet<EntityId> _orderedKeys = new();

    public int Count => _items.Count;

    public void Set(EntityId id, T component)
    {
        _items[id] = component;
        _orderedKeys.Add(id);
    }

    public bool Remove(EntityId id)
    {
        if (!_items.Remove(id))
        {
            return false;
        }

        _orderedKeys.Remove(id);
        return true;
    }

    public bool Contains(EntityId id) => _items.ContainsKey(id);

    public bool TryGet(EntityId id, out T component) => _items.TryGetValue(id, out component);

    /// <summary>
    /// Direct reference into the backing storage for in-place mutation.
    /// Throws <see cref="KeyNotFoundException"/> if the entity does not
    /// have a component of this type. The returned ref is valid until
    /// any structural edit of this store.
    /// </summary>
    public ref T GetRef(EntityId id)
    {
        ref T slot = ref CollectionsMarshal.GetValueRefOrNullRef(_items, id);
        if (Unsafe.IsNullRef(ref slot))
        {
            throw new KeyNotFoundException($"Entity {id} has no component of type {typeof(T).Name}.");
        }

        return ref slot!;
    }

    /// <summary>
    /// Deterministic ascending-by-<see cref="EntityId"/> enumeration of the
    /// entities in this store paired with their component values.
    /// </summary>
    public IEnumerable<(EntityId Id, T Component)> Enumerate()
    {
        foreach (var id in _orderedKeys)
        {
            yield return (id, _items[id]);
        }
    }

    /// <summary>
    /// Deterministic ascending iteration of the entity ids in this store,
    /// used by <see cref="EntityStore"/> to compute the union of all
    /// entities that have at least one component.
    /// </summary>
    public IEnumerable<EntityId> Keys => _orderedKeys;
}
