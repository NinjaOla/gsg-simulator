using SimEngine.Ids;
using SimEngine.State.Components;

namespace SimEngine.State;

/// <summary>
/// Programmatic builder for a <see cref="SimulationState"/>. Used by tests
/// that need a synthetic world, by games that generate worlds procedurally,
/// and — once Phase 1b lands — as the canonical "take seed data, produce
/// state" routine invoked by <see cref="IWorldLoader"/> implementations.
///
/// Instances are single-use. Call <see cref="AddProvince"/> and
/// <see cref="AddEdge"/> as needed, then <see cref="Build"/> once.
/// </summary>
public sealed class WorldBuilder
{
    private readonly List<(ProvinceId Id, ProvinceComponent Component)> _pendingProvinces = new();
    private readonly AdjacencyGraph.Builder _adjacencyBuilder = new();
    private readonly HashSet<ProvinceId> _declaredProvinces = new();
    private bool _built;
    private uint _nextProvinceValue = 1;

    /// <summary>
    /// Declares a new province. The returned <see cref="ProvinceId"/> is
    /// the value that the matching entity will carry after <see cref="Build"/>
    /// populates the state. Province ids are allocated densely from 1 so
    /// callers can reference them in subsequent calls.
    /// </summary>
    public ProvinceId AddProvince(
        string name,
        Terrain terrain = Terrain.Land,
        int latE6 = 0,
        int lonE6 = 0)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(name);

        var id = new ProvinceId(_nextProvinceValue++);
        _pendingProvinces.Add((id, new ProvinceComponent(name, terrain, latE6, lonE6)));
        _declaredProvinces.Add(id);
        _adjacencyBuilder.AddProvince(id);
        return id;
    }

    /// <summary>
    /// Declares an undirected edge between two provinces. Both endpoints
    /// must have been returned by a prior <see cref="AddProvince"/> call.
    /// </summary>
    public void AddEdge(ProvinceId a, ProvinceId b)
    {
        ThrowIfBuilt();

        if (!_declaredProvinces.Contains(a))
        {
            throw new ArgumentException($"Unknown province {a}. Call AddProvince first.", nameof(a));
        }

        if (!_declaredProvinces.Contains(b))
        {
            throw new ArgumentException($"Unknown province {b}. Call AddProvince first.", nameof(b));
        }

        _adjacencyBuilder.AddUndirectedEdge(a, b);
    }

    /// <summary>
    /// Produces a <see cref="SimulationState"/> with one entity per declared
    /// province, each carrying a <see cref="ProvinceComponent"/>, and an
    /// <see cref="AdjacencyGraph"/> containing every declared edge. The
    /// builder is marked consumed and cannot be reused.
    /// </summary>
    public SimulationState Build()
    {
        ThrowIfBuilt();
        _built = true;

        var adjacency = _adjacencyBuilder.Build();
        var state = new SimulationState(adjacency);

        // Allocate entities in ProvinceId order so the resulting EntityId
        // values match ProvinceId values exactly for this initial cohort.
        _pendingProvinces.Sort(static (x, y) => x.Id.CompareTo(y.Id));
        foreach (var (provinceId, component) in _pendingProvinces)
        {
            var entityId = state.Entities.Create();
            if (entityId.Value != provinceId.Value)
            {
                throw new InvalidOperationException(
                    $"EntityId allocation drifted from ProvinceId: expected {provinceId.Value}, got {entityId.Value}.");
            }

            state.Entities.Attach(entityId, component);
        }

        return state;
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException("WorldBuilder is single-use; create a new instance.");
        }
    }
}
