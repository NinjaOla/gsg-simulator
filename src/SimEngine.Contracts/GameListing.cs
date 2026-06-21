namespace SimEngine.Contracts;

/// <summary>
/// A game created through the lobby. Carries the <see cref="SessionId"/> a
/// client uses to connect to the <c>IGameSessionGrain</c>, plus enough metadata
/// to render a lobby row without touching engine state.
/// </summary>
[GenerateSerializer]
public sealed record GameListing
{
    /// <summary>Session/grain id to connect to.</summary>
    [Id(0)]
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Player-supplied display name for the game.</summary>
    [Id(1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Stable catalog id of the world the game was created from.</summary>
    [Id(2)]
    public string WorldId { get; init; } = string.Empty;

    /// <summary>Display name of the loaded world.</summary>
    [Id(3)]
    public string WorldName { get; init; } = string.Empty;

    /// <summary>When the game was created (server wall-clock; metadata only).</summary>
    [Id(4)]
    public DateTimeOffset CreatedAt { get; init; }
}
