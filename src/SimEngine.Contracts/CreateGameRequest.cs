namespace SimEngine.Contracts;

/// <summary>
/// Parameters for creating a new game through the lobby. The lobby generates
/// the session id; the caller only chooses the world, start date, and seed.
/// </summary>
[GenerateSerializer]
public sealed record CreateGameRequest
{
    /// <summary>Display name for the game shown in the lobby.</summary>
    [Id(0)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Stable catalog id of the world to load (see <c>ListWorldsAsync</c>).</summary>
    [Id(1)]
    public string WorldId { get; init; } = string.Empty;

    /// <summary>Simulation start date.</summary>
    [Id(2)]
    public DateTimeOffset StartDate { get; init; }

    /// <summary>Deterministic PRNG seed.</summary>
    [Id(3)]
    public ulong Seed { get; init; }
}
