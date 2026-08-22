namespace SimEngine.Contracts;

/// <summary>
/// A world a client may create a game from, exposed by the lobby so callers
/// that only reference the contracts never deal in server-side file paths.
/// </summary>
public sealed record WorldOption
{
    /// <summary>Stable catalog id passed back in <see cref="CreateGameRequest.WorldId"/>.</summary>
    public string WorldId { get; init; } = string.Empty;

    /// <summary>Human-readable world name for display.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
