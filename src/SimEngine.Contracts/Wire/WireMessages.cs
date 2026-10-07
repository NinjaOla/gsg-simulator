using System.Text.Json.Serialization;

namespace SimEngine.Contracts.Wire;

/// <summary>Wire protocol constants.</summary>
public static class WireProtocol
{
    /// <summary>Bumped on any incompatible wire change; checked in the handshake.</summary>
    public const int Version = 2;
}

/// <summary>
/// A message from a client to the server gateway. Only the types listed here
/// are accepted on the wire.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ClientMessages.Hello), "hello")]
[JsonDerivedType(typeof(ClientMessages.ListWorlds), "listWorlds")]
[JsonDerivedType(typeof(ClientMessages.ListGames), "listGames")]
[JsonDerivedType(typeof(ClientMessages.GetGame), "getGame")]
[JsonDerivedType(typeof(ClientMessages.CreateGame), "createGame")]
[JsonDerivedType(typeof(ClientMessages.LoadGame), "loadGame")]
[JsonDerivedType(typeof(ClientMessages.RemoveGame), "removeGame")]
[JsonDerivedType(typeof(ClientMessages.JoinGame), "joinGame")]
[JsonDerivedType(typeof(ClientMessages.LeaveGame), "leaveGame")]
[JsonDerivedType(typeof(ClientMessages.StartGame), "startGame")]
[JsonDerivedType(typeof(ClientMessages.UpdateSettings), "updateSettings")]
[JsonDerivedType(typeof(ClientMessages.SendCommands), "sendCommands")]
[JsonDerivedType(typeof(ClientMessages.DebugStep), "debugStep")]
[JsonDerivedType(typeof(ClientMessages.SaveGame), "saveGame")]
[JsonDerivedType(typeof(ClientMessages.GetStatus), "getStatus")]
[JsonDerivedType(typeof(ClientMessages.Subscribe), "subscribe")]
[JsonDerivedType(typeof(ClientMessages.Unsubscribe), "unsubscribe")]
[JsonDerivedType(typeof(ClientMessages.AckUpdate), "ackUpdate")]
public abstract record ClientMessage;

/// <summary>A client message that expects exactly one <see cref="ServerReply"/> with the same id.</summary>
public abstract record ClientRequest(long RequestId) : ClientMessage;

/// <summary>A request scoped to the session the connection has joined.</summary>
public abstract record SessionRequest(long RequestId, string SessionId) : ClientRequest(RequestId);

/// <summary>Client → server messages. The player id is never sent; it is bound to the connection.</summary>
public static class ClientMessages
{
    /// <summary>First message on a connection. Reply: <see cref="ServerMessages.WelcomeReply"/>.</summary>
    public sealed record Hello(long RequestId, int ProtocolVersion, string PlayerName) : ClientRequest(RequestId);

    /// <summary>Reply: <see cref="ServerMessages.WorldsReply"/>.</summary>
    public sealed record ListWorlds(long RequestId) : ClientRequest(RequestId);

    /// <summary>Reply: <see cref="ServerMessages.GamesReply"/>.</summary>
    public sealed record ListGames(long RequestId) : ClientRequest(RequestId);

    /// <summary>Reply: <see cref="ServerMessages.GameReply"/>.</summary>
    public sealed record GetGame(long RequestId, string SessionId) : ClientRequest(RequestId);

    /// <summary>Reply: <see cref="ServerMessages.GameReply"/>.</summary>
    public sealed record CreateGame(long RequestId, CreateGameRequest Request) : ClientRequest(RequestId);

    /// <summary>Creates a game from a save on the server. Reply: <see cref="ServerMessages.GameReply"/>.</summary>
    public sealed record LoadGame(long RequestId, string Name, string SavePath) : ClientRequest(RequestId);

    /// <summary>Creator or host only. Reply: <see cref="ServerMessages.OkReply"/>.</summary>
    public sealed record RemoveGame(long RequestId, string SessionId) : ClientRequest(RequestId);

    /// <summary>Joins a lobby, leaving any current session. Reply: <see cref="ServerMessages.OkReply"/>.</summary>
    public sealed record JoinGame(long RequestId, string SessionId, string ContentHash) : ClientRequest(RequestId);

    /// <summary>The host leaving ends the session. Reply: <see cref="ServerMessages.OkReply"/>.</summary>
    public sealed record LeaveGame(long RequestId, string SessionId) : SessionRequest(RequestId, SessionId);

    /// <summary>Host only. Reply: <see cref="ServerMessages.OkReply"/>.</summary>
    public sealed record StartGame(long RequestId, string SessionId, bool Paused) : SessionRequest(RequestId, SessionId);

    /// <summary>Host only, lobby only. Reply: <see cref="ServerMessages.OkReply"/>.</summary>
    public sealed record UpdateSettings(long RequestId, string SessionId, GameSettings Settings) : SessionRequest(RequestId, SessionId);

    /// <summary>Applies commands in order, stopping at the first rejection. Reply: <see cref="ServerMessages.OkReply"/>.</summary>
    public sealed record SendCommands(long RequestId, string SessionId, PlayerCommand[] Commands) : SessionRequest(RequestId, SessionId);

    /// <summary>Reply: <see cref="ServerMessages.TickReply"/>.</summary>
    public sealed record DebugStep(long RequestId, string SessionId, int Ticks) : SessionRequest(RequestId, SessionId);

    /// <summary>
    /// Host only. <paramref name="SavePath"/> is a file name resolved in the
    /// server's save directory. Reply: <see cref="ServerMessages.SavedReply"/>.
    /// </summary>
    public sealed record SaveGame(long RequestId, string SessionId, string SavePath) : SessionRequest(RequestId, SessionId);

    /// <summary>Reply: <see cref="ServerMessages.StatusReply"/>.</summary>
    public sealed record GetStatus(long RequestId, string SessionId) : SessionRequest(RequestId, SessionId);

    /// <summary>
    /// Starts the update stream, then replies with the baseline. Updates may
    /// arrive before the reply. Reply: <see cref="ServerMessages.SnapshotReply"/>.
    /// </summary>
    public sealed record Subscribe(long RequestId, string SessionId) : SessionRequest(RequestId, SessionId);

    /// <summary>Reply: <see cref="ServerMessages.OkReply"/>.</summary>
    public sealed record Unsubscribe(long RequestId, string SessionId) : SessionRequest(RequestId, SessionId);

    /// <summary>Acknowledges an applied update or snapshot. No reply.</summary>
    public sealed record AckUpdate(string SessionId, long Sequence) : ClientMessage;
}

/// <summary>A message from the server gateway to a client.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ServerMessages.WelcomeReply), "welcome")]
[JsonDerivedType(typeof(ServerMessages.OkReply), "ok")]
[JsonDerivedType(typeof(ServerMessages.ErrorReply), "error")]
[JsonDerivedType(typeof(ServerMessages.WorldsReply), "worlds")]
[JsonDerivedType(typeof(ServerMessages.GamesReply), "games")]
[JsonDerivedType(typeof(ServerMessages.GameReply), "game")]
[JsonDerivedType(typeof(ServerMessages.StatusReply), "status")]
[JsonDerivedType(typeof(ServerMessages.SnapshotReply), "snapshot")]
[JsonDerivedType(typeof(ServerMessages.TickReply), "tick")]
[JsonDerivedType(typeof(ServerMessages.SavedReply), "saved")]
[JsonDerivedType(typeof(ServerMessages.StreamUpdatePush), "streamUpdate")]
[JsonDerivedType(typeof(ServerMessages.StreamSnapshotPush), "streamSnapshot")]
[JsonDerivedType(typeof(ServerMessages.SessionEndedPush), "sessionEnded")]
[JsonDerivedType(typeof(ServerMessages.DisconnectingPush), "disconnecting")]
public abstract record ServerMessage;

/// <summary>The reply to the <see cref="ClientRequest"/> with the same <see cref="RequestId"/>.</summary>
public abstract record ServerReply(long RequestId) : ServerMessage;

/// <summary>Server → client messages: replies and pushes.</summary>
public static class ServerMessages
{
    public sealed record WelcomeReply(long RequestId, string PlayerId, int ProtocolVersion) : ServerReply(RequestId);

    public sealed record OkReply(long RequestId) : ServerReply(RequestId);

    /// <summary>
    /// A rejected request. <see cref="ExpectedHash"/>/<see cref="ActualHash"/>
    /// are set for <see cref="WireErrorCode.ContentMismatch"/>.
    /// </summary>
    public sealed record ErrorReply(
        long RequestId,
        WireErrorCode Code,
        string Message,
        string? ExpectedHash = null,
        string? ActualHash = null) : ServerReply(RequestId);

    public sealed record WorldsReply(long RequestId, WorldOption[] Worlds) : ServerReply(RequestId);

    public sealed record GamesReply(long RequestId, GameListing[] Games) : ServerReply(RequestId);

    public sealed record GameReply(long RequestId, GameListing? Game) : ServerReply(RequestId);

    public sealed record StatusReply(long RequestId, SessionStatus Status) : ServerReply(RequestId);

    public sealed record SnapshotReply(long RequestId, SessionSnapshot Snapshot) : ServerReply(RequestId);

    public sealed record TickReply(long RequestId, TickResult Result) : ServerReply(RequestId);

    public sealed record SavedReply(long RequestId, string Path) : ServerReply(RequestId);

    /// <summary>A per-tick (or merged catch-up) update; the client acks its sequence.</summary>
    public sealed record StreamUpdatePush(SessionStreamUpdate Update) : ServerMessage;

    /// <summary>A resync snapshot for a client that fell too far behind; the client acks its sequence.</summary>
    public sealed record StreamSnapshotPush(SessionSnapshot Snapshot) : ServerMessage;

    /// <summary>The session ended; the client is no longer a member.</summary>
    public sealed record SessionEndedPush(string SessionId, string Reason) : ServerMessage;

    /// <summary>The server is about to close the connection.</summary>
    public sealed record DisconnectingPush(string Reason) : ServerMessage;
}

/// <summary>Why a request was rejected.</summary>
public enum WireErrorCode
{
    Internal = 0,
    BadRequest,
    ProtocolVersionMismatch,
    HandshakeRequired,
    NotJoined,
    NotAuthorized,
    UnknownSession,
    ContentMismatch,
    CommandRejected,
    InvalidState,
    IoError,
    Timeout,
}
