namespace SimEngine.Contracts;

/// <summary>
/// Pushed to the per-session stream after each step so clients can render
/// without polling. Events are pre-rendered plain text for now; typed event
/// DTOs replace this when the client-side state model lands.
/// </summary>
public sealed record SessionStreamUpdate
{
    /// <summary>Result of the step that produced this update.</summary>
    public TickResult Tick { get; init; } = new();

    /// <summary>Game events emitted during the step, rendered as plain text.</summary>
    public string[] Events { get; init; } = [];

    /// <summary>Countries whose treasury changed during the step (absolute balances).</summary>
    public CountryTreasuryDelta[] CountryDeltas { get; init; } = [];
}
