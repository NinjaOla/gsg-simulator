namespace SimEngine.Contracts;

/// <summary>
/// A full baseline of a session's synced read model, fetched once when a client
/// connects. After applying this, the client folds in per-tick
/// <see cref="SessionStreamUpdate"/> messages to stay current without polling
/// engine state.
/// </summary>
/// <remarks>
/// Memory streams do not replay history to late subscribers, so a client must
/// fetch this snapshot explicitly (see <c>IGameSessionGrain.GetSnapshotAsync</c>)
/// and then subscribe for deltas.
/// </remarks>
[GenerateSerializer]
public sealed record SessionSnapshot
{
    /// <summary>Display name of the loaded world.</summary>
    [Id(0)]
    public string WorldName { get; init; } = string.Empty;

    /// <summary>Current tick number at the time the snapshot was taken.</summary>
    [Id(1)]
    public long TickNumber { get; init; }

    /// <summary>Current simulation date at the time the snapshot was taken.</summary>
    [Id(2)]
    public DateTimeOffset CurrentDate { get; init; }

    /// <summary>Number of provinces in the world (static).</summary>
    [Id(3)]
    public int ProvinceCount { get; init; }

    /// <summary>Number of undirected adjacency edges in the world (static).</summary>
    [Id(4)]
    public int AdjacencyEdgeCount { get; init; }

    /// <summary>All countries with their baseline treasury balances.</summary>
    /// <remarks>Concrete array: Orleans has no codec for the compiler-synthesized
    /// wrapper that collection expressions produce for interface-typed targets.</remarks>
    [Id(5)]
    public CountryState[] Countries { get; init; } = [];
}
