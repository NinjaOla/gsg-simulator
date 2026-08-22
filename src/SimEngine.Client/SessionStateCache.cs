using SimEngine.Contracts;

namespace SimEngine.Client;

/// <summary>
/// Client-side read model for one game session. Built from a
/// <see cref="SessionSnapshot"/> fetched on connect, then kept current by
/// folding in <see cref="SessionStreamUpdate"/> messages from the session
/// stream. This lets a client render without holding an in-process engine view.
/// </summary>
/// <remarks>
/// Thread-safe: <see cref="Apply"/> runs on Akka dispatcher threads while a
/// render loop reads concurrently, so all access is guarded.
/// </remarks>
public sealed class SessionStateCache
{
    private const int MaxEvents = 200;

    private readonly object _gate = new();
    private readonly Dictionary<string, CountryState> _countries = new(StringComparer.Ordinal);
    private readonly List<string> _events = [];

    private string _worldName = string.Empty;
    private long _tickNumber;
    private DateTimeOffset _currentDate;
    private int _provinceCount;
    private int _adjacencyEdgeCount;

    /// <summary>Creates a cache seeded from an initial snapshot.</summary>
    public SessionStateCache(SessionSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    /// <summary>Display name of the loaded world.</summary>
    public string WorldName
    {
        get { lock (_gate) { return _worldName; } }
    }

    /// <summary>Most recently observed tick number.</summary>
    public long TickNumber
    {
        get { lock (_gate) { return _tickNumber; } }
    }

    /// <summary>Most recently observed simulation date.</summary>
    public DateTimeOffset CurrentDate
    {
        get { lock (_gate) { return _currentDate; } }
    }

    /// <summary>Number of provinces in the world (static).</summary>
    public int ProvinceCount
    {
        get { lock (_gate) { return _provinceCount; } }
    }

    /// <summary>Number of undirected adjacency edges in the world (static).</summary>
    public int AdjacencyEdgeCount
    {
        get { lock (_gate) { return _adjacencyEdgeCount; } }
    }

    /// <summary>All countries, ordered by tag for deterministic rendering.</summary>
    public IReadOnlyList<CountryState> Countries
    {
        get
        {
            lock (_gate)
            {
                return _countries.Values
                    .OrderBy(c => c.Tag, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    /// <summary>The most recent game events, oldest first (capped).</summary>
    public IReadOnlyList<string> Events
    {
        get { lock (_gate) { return _events.ToArray(); } }
    }

    /// <summary>Gets a country's current state by tag, or <c>false</c> if unknown.</summary>
    public bool TryGetCountry(string tag, out CountryState country)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        lock (_gate)
        {
            return _countries.TryGetValue(tag, out country!);
        }
    }

    /// <summary>Replaces the cached baseline with a fresh snapshot.</summary>
    public void ApplySnapshot(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            _worldName = snapshot.WorldName;
            _tickNumber = snapshot.TickNumber;
            _currentDate = snapshot.CurrentDate;
            _provinceCount = snapshot.ProvinceCount;
            _adjacencyEdgeCount = snapshot.AdjacencyEdgeCount;

            _countries.Clear();
            foreach (var country in snapshot.Countries)
            {
                _countries[country.Tag] = country;
            }
        }
    }

    /// <summary>
    /// Folds a per-tick update into the cache: advances tick/date, applies
    /// absolute treasury balances for changed countries, and appends events.
    /// </summary>
    public void Apply(SessionStreamUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_gate)
        {
            _tickNumber = update.Tick.TickNumber;
            _currentDate = update.Tick.CurrentDate;

            foreach (var delta in update.CountryDeltas)
            {
                _countries[delta.Tag] = _countries.TryGetValue(delta.Tag, out var existing)
                    ? existing with { FundsE2 = delta.FundsE2 }
                    : new CountryState { Tag = delta.Tag, DisplayName = delta.Tag, FundsE2 = delta.FundsE2 };
            }

            foreach (var entry in update.Events)
            {
                _events.Add(entry);
            }

            if (_events.Count > MaxEvents)
            {
                _events.RemoveRange(0, _events.Count - MaxEvents);
            }
        }
    }
}
