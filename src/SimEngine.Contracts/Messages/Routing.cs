namespace SimEngine.Contracts;

/// <summary>
/// Marker for a message routed to a specific game-session entity actor. The
/// <see cref="SessionId"/> is the entity/shard key.
/// </summary>
public interface IWithSessionId
{
    /// <summary>The target session id.</summary>
    string SessionId { get; }
}

/// <summary>
/// Marker for a message routed to a specific player entity actor. The
/// <see cref="PlayerId"/> is the entity/shard key.
/// </summary>
public interface IWithPlayerId
{
    /// <summary>The target player id.</summary>
    string PlayerId { get; }
}

/// <summary>
/// Generic acknowledgement reply for commands that do not return a value.
/// </summary>
public sealed record Ack
{
    /// <summary>Shared instance.</summary>
    public static readonly Ack Instance = new();
}

/// <summary>
/// Well-known top-level actor names hosted by the server actor system. Clients
/// resolve these by name (locally) or by remote path (clustered/remoting).
/// </summary>
public static class ActorNames
{
    /// <summary>The single shared lobby actor.</summary>
    public const string Lobby = "lobby";

    /// <summary>The child-per-entity parent that owns game-session actors.</summary>
    public const string Sessions = "sessions";

    /// <summary>The child-per-entity parent that owns player actors.</summary>
    public const string Players = "players";
}

/// <summary>
/// Registry marker for the game-session entity host (child-per-entity parent in
/// local mode, shard region in clustered mode). Used to resolve the host from
/// the Akka <c>ActorRegistry</c> in both server and in-process client code.
/// </summary>
public sealed class SessionActorsMarker;

/// <summary>
/// Registry marker for the player entity host.
/// </summary>
public sealed class PlayerActorsMarker;

/// <summary>
/// Registry marker for the singleton lobby actor.
/// </summary>
public sealed class LobbyMarker;
