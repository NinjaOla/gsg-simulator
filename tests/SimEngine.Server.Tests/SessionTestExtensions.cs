using SimEngine.Contracts;
using SimEngine.Server.Direct;

namespace SimEngine.Server.Tests;

/// <summary>
/// Test helpers that put a session into a paused, debug-enabled state so
/// tests can step a deterministic number of ticks via <c>DebugStep</c>.
/// </summary>
internal static class SessionTestExtensions
{
    public const string HostId = "host";

    /// <summary>Initializes a new game and prepares it for debug stepping.</summary>
    public static async Task<SessionInfo> InitializeForDebugAsync(
        this ActorSessionClient session, string worldId, DateTimeOffset startDate, ulong seed, CancellationToken ct)
    {
        var info = await session.InitializeAsync(worldId, startDate, seed, ct);
        await session.PrepareForDebugAsync(info.ContentHash, ct);
        return info;
    }

    /// <summary>Joins as host, enables debug commands, and starts the game paused.</summary>
    public static async Task PrepareForDebugAsync(this ActorSessionClient session, string contentHash, CancellationToken ct)
    {
        await session.JoinAsync(HostId, contentHash, ct);
        await session.UpdateSettingsAsync(HostId, new GameSettings { AllowDebugCommands = true }, ct);
        await session.StartGameAsync(HostId, paused: true, ct);
    }

    /// <summary>Debug-steps as the host.</summary>
    public static Task<TickResult> StepAsync(this ActorSessionClient session, int ticks, CancellationToken ct) =>
        session.DebugStepAsync(HostId, ticks, ct);
}
