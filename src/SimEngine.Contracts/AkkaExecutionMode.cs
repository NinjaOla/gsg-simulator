namespace SimEngine.Contracts;

/// <summary>
/// Determines how the SimEngine actor systems are configured.
/// </summary>
public enum AkkaExecutionMode
{
    /// <summary>
    /// Pure local actor system: no remoting, no clustering. Entity actors are
    /// hosted under a lightweight child-per-entity parent. Ideal for tests and
    /// single-player.
    /// </summary>
    LocalTest,

    /// <summary>
    /// Remote/clustered hosting so external client processes can attach to a
    /// shared server. Used for dedicated multiplayer servers.
    /// </summary>
    Clustered,
}
