namespace SimEngine.Contracts;

/// <summary>
/// Summary of a game session, returned after initialization. Enough for a
/// client to render a lobby/loading line without touching engine state.
/// </summary>
public sealed record SessionInfo
{
    /// <summary>Display name of the loaded world.</summary>
    public string WorldName { get; init; } = string.Empty;

    /// <summary>Current tick number (0 for a fresh game).</summary>
    public long TickNumber { get; init; }

    /// <summary>Current simulation date.</summary>
    public DateTimeOffset CurrentDate { get; init; }

    /// <summary>Number of provinces in the world.</summary>
    public int ProvinceCount { get; init; }

    /// <summary>Number of undirected adjacency edges in the world.</summary>
    public int AdjacencyEdgeCount { get; init; }
}
