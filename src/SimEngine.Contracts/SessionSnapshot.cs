namespace SimEngine.Contracts;

/// <summary>
/// A full baseline of a session's synced read model, fetched once when a client
/// connects. After applying this, the client folds in per-tick
/// <see cref="SessionStreamUpdate"/> messages to stay current without polling
/// engine state.
/// </summary>
/// <remarks>
/// Session broadcasts do not replay history to late subscribers, so a client must
/// fetch this snapshot explicitly (see <c>SessionProtocol.GetSnapshot</c>)
/// and then subscribe for deltas.
/// </remarks>
public sealed record SessionSnapshot
{
    /// <summary>The session this snapshot describes.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Display name of the loaded world.</summary>
    public string WorldName { get; init; } = string.Empty;

    /// <summary>Stable world id; clients load this world's static data locally.</summary>
    public string WorldId { get; init; } = string.Empty;

    /// <summary>The session's authoritative content hash, checked on join.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>Current tick number at the time the snapshot was taken.</summary>
    public long TickNumber { get; init; }

    /// <summary>Current simulation date at the time the snapshot was taken.</summary>
    public DateTimeOffset CurrentDate { get; init; }

    /// <summary>Number of provinces in the world (static).</summary>
    public int ProvinceCount { get; init; }

    /// <summary>Number of undirected adjacency edges in the world (static).</summary>
    public int AdjacencyEdgeCount { get; init; }

    /// <summary>All countries with their baseline treasury balances.</summary>
    public CountryState[] Countries { get; init; } = [];

    /// <summary>Last published update sequence covered by this snapshot.</summary>
    public long Sequence { get; init; }

    /// <summary>Current lifecycle stage.</summary>
    public SessionStage Stage { get; init; }

    /// <summary>Current speed level.</summary>
    public int Speed { get; init; }

    /// <summary>The host player, or <c>null</c> if nobody has joined.</summary>
    public string? HostPlayerId { get; init; }

    /// <summary>The game's settings.</summary>
    public GameSettings Settings { get; init; } = new();

    /// <summary>Ordered session members.</summary>
    public PlayerInfo[] Players { get; init; } = [];
}
