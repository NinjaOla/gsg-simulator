using Akka.Actor;

namespace SimEngine.Contracts;

/// <summary>
/// Message protocol for a game-session entity actor. Each session owns one
/// authoritative <c>SimulationEngine</c>; the actor is the single simulation
/// authority. Replies are noted per message.
/// </summary>
public static class SessionProtocol
{
    /// <summary>
    /// Initializes a new game (loads the world, seeds, builds the engine).
    /// Reply: <see cref="SessionInfo"/>.
    /// </summary>
    public sealed record Initialize(string SessionId, string WorldId, DateTimeOffset StartDate, ulong Seed)
        : IWithSessionId;

    /// <summary>
    /// Initializes the session from a save file on the server's file system.
    /// Reply: <see cref="SessionInfo"/>.
    /// </summary>
    public sealed record InitializeFromSave(string SessionId, string SavePath) : IWithSessionId;

    /// <summary>
    /// Applies a command issued by <paramref name="PlayerId"/>. Reply: <see cref="Ack"/>,
    /// or <see cref="Status.Failure"/> wrapping <see cref="CommandRejectedException"/>.
    /// </summary>
    public sealed record EnqueueCommand(string SessionId, string PlayerId, PlayerCommand Command) : IWithSessionId;

    /// <summary>
    /// Applies a batch of commands in order, stopping at the first rejection.
    /// Reply: <see cref="Ack"/>, or <see cref="Status.Failure"/>.
    /// </summary>
    public sealed record EnqueueCommands(string SessionId, string PlayerId, PlayerCommand[] Commands) : IWithSessionId;

    /// <summary>
    /// Registers a player as a member (lobby only). The first player to join
    /// becomes host. Reply: <see cref="Ack"/>, or <see cref="Status.Failure"/>
    /// wrapping <see cref="ContentMismatchException"/> or <see cref="CommandRejectedException"/>.
    /// </summary>
    public sealed record Join(string SessionId, string PlayerId, string ContentHash, string? PlayerName = null) : IWithSessionId;

    /// <summary>
    /// Host-only: moves the session from <see cref="SessionStage.Lobby"/> to
    /// <see cref="SessionStage.Running"/> (or <see cref="SessionStage.Paused"/>
    /// when <paramref name="Paused"/>). Reply: <see cref="Ack"/>.
    /// </summary>
    public sealed record StartGame(string SessionId, string PlayerId, bool Paused = false) : IWithSessionId;

    /// <summary>Host-only, lobby-only: replaces the game settings. Reply: <see cref="Ack"/>.</summary>
    public sealed record UpdateSettings(string SessionId, string PlayerId, GameSettings Settings) : IWithSessionId;

    /// <summary>
    /// Debug: runs <paramref name="Ticks"/> ticks immediately. Only while paused
    /// and when <see cref="GameSettings.AllowDebugCommands"/>. Reply: <see cref="TickResult"/>.
    /// </summary>
    public sealed record DebugStep(string SessionId, string PlayerId, int Ticks) : IWithSessionId;

    /// <summary>Gets the lightweight session status. Reply: <see cref="SessionStatus"/>.</summary>
    public sealed record GetStatus(string SessionId) : IWithSessionId;

    /// <summary>
    /// Sent by a subscriber (as <c>Sender</c>) after applying the update or
    /// snapshot with <paramref name="Sequence"/>. No reply.
    /// </summary>
    public sealed record AckUpdate(string SessionId, long Sequence) : IWithSessionId;

    /// <summary>Removes a player. Reply: <see cref="Ack"/>.</summary>
    public sealed record Leave(string SessionId, string PlayerId) : IWithSessionId;

    /// <summary>
    /// The player's connection dropped. In the lobby this is a <see cref="Leave"/>; after
    /// the start the player stays a member, marked disconnected. The host disconnecting
    /// ends the session. Reply: <see cref="Ack"/>.
    /// </summary>
    public sealed record Disconnect(string SessionId, string PlayerId) : IWithSessionId;

    /// <summary>Gets the ordered session member ids. Reply: <c>string[]</c>.</summary>
    public sealed record GetPlayers(string SessionId) : IWithSessionId;

    /// <summary>Gets the full synced baseline. Reply: <see cref="SessionSnapshot"/>.</summary>
    public sealed record GetSnapshot(string SessionId) : IWithSessionId;

    /// <summary>Saves the engine snapshot. Reply: resolved absolute path (<c>string</c>).</summary>
    public sealed record Save(string SessionId, string SavePath) : IWithSessionId;

    /// <summary>Gets the current tick number. Reply: <c>long</c>.</summary>
    public sealed record GetTickNumber(string SessionId) : IWithSessionId;

    /// <summary>Gets the current simulation date. Reply: <see cref="DateTimeOffset"/>.</summary>
    public sealed record GetCurrentDate(string SessionId) : IWithSessionId;

    /// <summary>Ends the session and stops the actor. Reply: <see cref="Ack"/>.</summary>
    public sealed record Shutdown(string SessionId) : IWithSessionId;

    /// <summary>
    /// Subscribes <paramref name="Subscriber"/> to per-tick
    /// <see cref="SessionStreamUpdate"/> broadcasts. The subscriber must reply
    /// with <see cref="AckUpdate"/>; a lagging subscriber receives merged
    /// catch-up updates or a fresh <see cref="SessionSnapshot"/>. Reply: <see cref="Ack"/>.
    /// </summary>
    public sealed record Subscribe(string SessionId, IActorRef Subscriber) : IWithSessionId;

    /// <summary>Unsubscribes a previously subscribed actor. Reply: <see cref="Ack"/>.</summary>
    public sealed record Unsubscribe(string SessionId, IActorRef Subscriber) : IWithSessionId;

    /// <summary>
    /// Pushed to subscribers when the session ends (host left or shut down).
    /// No further updates follow.
    /// </summary>
    public sealed record SessionEnded(string SessionId, string Reason);
}
