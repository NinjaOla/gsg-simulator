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

    /// <summary>Queues a single command. Reply: <see cref="Ack"/>.</summary>
    public sealed record EnqueueCommand(string SessionId, PlayerCommand Command) : IWithSessionId;

    /// <summary>Queues a batch of commands in order. Reply: <see cref="Ack"/>.</summary>
    public sealed record EnqueueCommands(string SessionId, PlayerCommand[] Commands) : IWithSessionId;

    /// <summary>
    /// Registers a player as a member. Reply: <see cref="Ack"/>, or
    /// <see cref="Status.Failure"/> wrapping <see cref="ContentMismatchException"/>.
    /// </summary>
    public sealed record Join(string SessionId, string PlayerId, string ContentHash) : IWithSessionId;

    /// <summary>Removes a player. Reply: <see cref="Ack"/>.</summary>
    public sealed record Leave(string SessionId, string PlayerId) : IWithSessionId;

    /// <summary>Gets the ordered session members. Reply: <c>string[]</c>.</summary>
    public sealed record GetPlayers(string SessionId) : IWithSessionId;

    /// <summary>Gets the full synced baseline. Reply: <see cref="SessionSnapshot"/>.</summary>
    public sealed record GetSnapshot(string SessionId) : IWithSessionId;

    /// <summary>
    /// Applies queued commands then advances the engine. Reply: <see cref="TickResult"/>.
    /// </summary>
    public sealed record Advance(string SessionId, int Ticks) : IWithSessionId;

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
    /// <see cref="SessionStreamUpdate"/> broadcasts. Reply: <see cref="Ack"/>.
    /// </summary>
    public sealed record Subscribe(string SessionId, IActorRef Subscriber) : IWithSessionId;

    /// <summary>Unsubscribes a previously subscribed actor. Reply: <see cref="Ack"/>.</summary>
    public sealed record Unsubscribe(string SessionId, IActorRef Subscriber) : IWithSessionId;
}
