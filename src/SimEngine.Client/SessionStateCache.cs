using SimEngine.Contracts;

namespace SimEngine.Client;

/// <summary>
/// Client-side read model for one game session. Built from a
/// <see cref="SessionSnapshot"/> fetched on connect, then kept current by
/// folding in <see cref="SessionStreamUpdate"/> messages from the session
/// stream. This lets a client render without holding an in-process engine view.
/// </summary>
/// <remarks>
/// Thread-safe: updates are applied on the connection's read thread while a
/// render loop reads concurrently, so all access is guarded.
/// </remarks>
public sealed class SessionStateCache
{
    private const int MaxEvents = 200;

    private readonly object _gate = new();
    private readonly Dictionary<string, CountryState> _countries = new(StringComparer.Ordinal);
    private readonly List<string> _events = [];

    private string _worldName = string.Empty;
    private string _worldId = string.Empty;
    private long _tickNumber;
    private DateTimeOffset _currentDate;
    private int _provinceCount;
    private int _adjacencyEdgeCount;
    private long _sequence;
    private SessionStage _stage;
    private int _speed;
    private string? _hostPlayerId;
    private GameSettings _settings = new();
    private PlayerInfo[] _players = [];
    private string? _endReason;

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

    /// <summary>Stable id of the loaded world.</summary>
    public string WorldId
    {
        get { lock (_gate) { return _worldId; } }
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

    /// <summary>Sequence of the last applied snapshot or update.</summary>
    public long Sequence
    {
        get { lock (_gate) { return _sequence; } }
    }

    /// <summary>Most recently observed session stage.</summary>
    public SessionStage Stage
    {
        get { lock (_gate) { return _stage; } }
    }

    /// <summary>Most recently observed speed level.</summary>
    public int Speed
    {
        get { lock (_gate) { return _speed; } }
    }

    /// <summary>Most recently observed host player.</summary>
    public string? HostPlayerId
    {
        get { lock (_gate) { return _hostPlayerId; } }
    }

    /// <summary>Most recently observed game settings.</summary>
    public GameSettings Settings
    {
        get { lock (_gate) { return _settings; } }
    }

    /// <summary>Most recently observed session members.</summary>
    public IReadOnlyList<PlayerInfo> Players
    {
        get { lock (_gate) { return _players; } }
    }

    /// <summary>Why the session ended (host left, closed, connection lost), or <c>null</c> while it is live.</summary>
    public string? EndReason
    {
        get { lock (_gate) { return _endReason; } }
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

    /// <summary>
    /// Blocks until the cache has observed <paramref name="tickNumber"/> (updates
    /// arrive asynchronously after an advance reply). Returns <c>false</c> on timeout.
    /// </summary>
    public bool WaitForTick(long tickNumber, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_tickNumber < tickNumber && _endReason is null)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(_gate, remaining))
                {
                    return _tickNumber >= tickNumber;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Blocks until <see cref="Sequence"/> differs from <paramref name="sequence"/>,
    /// the session ends, or the timeout elapses. Returns whether a change was
    /// observed; lets a render loop redraw on change instead of polling.
    /// </summary>
    public bool WaitForChange(long sequence, TimeSpan timeout)
    {
        lock (_gate)
        {
            if (_sequence == sequence && _endReason is null)
            {
                Monitor.Wait(_gate, timeout);
            }

            return _sequence != sequence || _endReason is not null;
        }
    }

    /// <summary>Marks the session as ended; no further updates will arrive.</summary>
    public void MarkEnded(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_gate)
        {
            _stage = SessionStage.Ended;
            _endReason ??= reason;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Replaces the cached baseline with a fresh snapshot.</summary>
    public void ApplySnapshot(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            _worldName = snapshot.WorldName;
            _worldId = snapshot.WorldId;
            _tickNumber = snapshot.TickNumber;
            _currentDate = snapshot.CurrentDate;
            _provinceCount = snapshot.ProvinceCount;
            _adjacencyEdgeCount = snapshot.AdjacencyEdgeCount;
            _sequence = snapshot.Sequence;
            _stage = snapshot.Stage;
            _speed = snapshot.Speed;
            _hostPlayerId = snapshot.HostPlayerId;
            _settings = snapshot.Settings;
            _players = snapshot.Players;

            _countries.Clear();
            foreach (var country in snapshot.Countries)
            {
                _countries[country.Tag] = country;
            }

            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>
    /// Folds an update into the cache: advances tick/date/stage/speed, takes the
    /// latest host, settings, and players, applies
    /// absolute treasury balances for changed countries, and appends events.
    /// Updates already covered (sequence not newer) are ignored.
    /// </summary>
    public void Apply(SessionStreamUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_gate)
        {
            if (update.Sequence <= _sequence)
            {
                return;
            }

            _sequence = update.Sequence;
            _stage = update.Stage;
            _speed = update.Speed;
            _hostPlayerId = update.HostPlayerId;
            _settings = update.Settings;
            _players = update.Players;
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

            Monitor.PulseAll(_gate);
        }
    }
}
