namespace SimEngine.Contracts;

/// <summary>
/// Thrown when a session rejects a command or lifecycle request: the issuer is
/// not a member, lacks permission under the game's <see cref="GameSettings"/>,
/// or the session is in the wrong <see cref="SessionStage"/>.
/// </summary>
public sealed class CommandRejectedException : Exception
{
    /// <summary>Creates the exception with a human-readable rejection reason.</summary>
    public CommandRejectedException(string reason)
        : base(reason)
    {
    }
}
