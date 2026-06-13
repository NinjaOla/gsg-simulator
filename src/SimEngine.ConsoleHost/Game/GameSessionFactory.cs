using Microsoft.Extensions.DependencyInjection;
using SimEngine.Contracts;
using SimEngine.Server;

namespace SimEngine.ConsoleHost.Game;

/// <summary>
/// Creates <see cref="GameSession"/> handles by driving the in-process
/// session grain. The grain owns the engine; the factory fetches the
/// read-only view from <see cref="ILocalEngineProvider"/> after the grain
/// has initialized.
/// </summary>
internal static class GameSessionFactory
{
    public static GameSession CreateNew(
        IServiceProvider services,
        string worldId,
        DateTimeOffset startDate,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);

        return Create(services, grain => grain.InitializeAsync(worldId, startDate, seed));
    }

    public static GameSession Load(string path, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var resolvedPath = SaveGamePaths.Resolve(path);
        return Create(services, grain => grain.InitializeFromSaveAsync(resolvedPath));
    }

    public static string Save(GameSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(session);

        var resolvedPath = SaveGamePaths.Resolve(path);
        return session.Grain.SaveAsync(resolvedPath).GetAwaiter().GetResult();
    }

    private static GameSession Create(
        IServiceProvider services,
        Func<IGameSessionGrain, Task<SessionInfo>> initialize)
    {
        var client = services.GetRequiredService<IClusterClient>();
        var engineProvider = services.GetRequiredService<ILocalEngineProvider>();

        var sessionId = Guid.NewGuid().ToString("N");
        var grain = client.GetGrain<IGameSessionGrain>(sessionId);

        // Console host is sync throughout; blocking here is safe (no sync context).
        var info = initialize(grain).GetAwaiter().GetResult();

        var engine = engineProvider.GetEngine(sessionId)
            ?? throw new InvalidOperationException(
                $"Session '{sessionId}' has no in-process engine. The console host requires the silo to run in-process.");

        return new GameSession(engine, grain, info.WorldName, sessionId, services);
    }
}
