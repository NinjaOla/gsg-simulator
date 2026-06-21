namespace SimEngine.Contracts;

/// <summary>
/// Grain interface for a single player. A player buffers its own commands and
/// joins/leaves a game session; flushing forwards the buffered commands to the
/// session grain, which applies them at the next step boundary. The grain key
/// is the player ID (string).
/// </summary>
public interface IPlayerGrain : IGrainWithStringKey
{
    /// <summary>
    /// Joins the session identified by <paramref name="sessionId"/>. Idempotent
    /// for the same session; throws if the player is already in a different
    /// session (leave it first).
    /// <para>
    /// <paramref name="contentHash"/> is the client's locally computed content
    /// hash; it is forwarded to the session, which rejects a mismatch with
    /// <see cref="ContentMismatchException"/> once it has loaded its content.
    /// </para>
    /// </summary>
    Task JoinSessionAsync(string sessionId, string contentHash);

    /// <summary>
    /// Leaves the current session and discards any buffered commands. No-op if
    /// the player has not joined a session.
    /// </summary>
    Task LeaveSessionAsync();

    /// <summary>
    /// Buffers a command locally. Throws if the player has not joined a session.
    /// </summary>
    Task SubmitCommandAsync(PlayerCommand command);

    /// <summary>
    /// Forwards all buffered commands (in order) to the joined session, then
    /// clears the buffer. Returns the number of commands flushed. Throws if the
    /// player has not joined a session.
    /// </summary>
    Task<int> FlushCommandsAsync();

    /// <summary>Gets the joined session ID, or <c>null</c> if not in a session.</summary>
    Task<string?> GetCurrentSessionAsync();

    /// <summary>Gets the number of commands currently buffered.</summary>
    Task<int> GetBufferedCommandCountAsync();
}
