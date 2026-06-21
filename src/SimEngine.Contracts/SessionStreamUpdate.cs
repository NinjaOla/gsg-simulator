namespace SimEngine.Contracts;

/// <summary>
/// Pushed to the per-session stream after each step so clients can render
/// without polling. Events are pre-rendered plain text for now; typed event
/// DTOs replace this when the client-side state model lands.
/// </summary>
[GenerateSerializer]
public sealed record SessionStreamUpdate
{
    /// <summary>Result of the step that produced this update.</summary>
    [Id(0)]
    public TickResult Tick { get; init; } = new();

    /// <summary>Game events emitted during the step, rendered as plain text.</summary>
    /// <remarks>Concrete array: Orleans has no codec for the compiler-synthesized
    /// wrapper that collection expressions produce for interface-typed targets.</remarks>
    [Id(1)]
    public string[] Events { get; init; } = [];

    /// <summary>Countries whose treasury changed during the step (absolute balances).</summary>
    /// <remarks>Only changed countries are included, keeping the message small at
    /// continent scale. Concrete array for the same Orleans-codec reason as <see cref="Events"/>.</remarks>
    [Id(2)]
    public CountryTreasuryDelta[] CountryDeltas { get; init; } = [];
}
