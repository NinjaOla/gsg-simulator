namespace SimEngine.Contracts;

/// <summary>
/// Message protocol for a player entity actor. A player buffers its own
/// commands and joins/leaves a game session; flushing forwards buffered
/// commands to the session actor.
/// </summary>
public static class PlayerProtocol
{
    /// <summary>
    /// Joins a session. Reply: <see cref="Ack"/>, or <c>Status.Failure</c>
    /// (already in a different session, or content mismatch).
    /// </summary>
    public sealed record JoinSession(string PlayerId, string SessionId, string ContentHash) : IWithPlayerId;

    /// <summary>Leaves the current session and discards buffered commands. Reply: <see cref="Ack"/>.</summary>
    public sealed record LeaveSession(string PlayerId) : IWithPlayerId;

    /// <summary>Buffers a command locally. Reply: <see cref="Ack"/> (fails if not joined).</summary>
    public sealed record SubmitCommand(string PlayerId, PlayerCommand Command) : IWithPlayerId;

    /// <summary>Flushes buffered commands to the joined session. Reply: count (<c>int</c>).</summary>
    public sealed record FlushCommands(string PlayerId) : IWithPlayerId;

    /// <summary>Gets the joined session id. Reply: <see cref="CurrentSessionResult"/>.</summary>
    public sealed record GetCurrentSession(string PlayerId) : IWithPlayerId;

    /// <summary>Wraps a nullable current session so Ask never returns a bare null.</summary>
    public sealed record CurrentSessionResult(string? SessionId);

    /// <summary>Gets the number of buffered commands. Reply: <c>int</c>.</summary>
    public sealed record GetBufferedCommandCount(string PlayerId) : IWithPlayerId;
}
