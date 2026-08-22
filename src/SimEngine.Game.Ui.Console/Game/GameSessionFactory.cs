using Microsoft.Extensions.DependencyInjection;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Server;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>
/// Creates <see cref="GameSession"/> handles by driving the in-process session
/// actor through the Akka <see cref="GameClient"/>. The session actor owns the
/// engine; the factory fetches the read-only view from
/// <see cref="ILocalEngineProvider"/> after the actor has initialized.
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

        return Create(services, session => session.InitializeAsync(worldId, startDate, seed));
    }

    public static GameSession Load(string path, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var resolvedPath = SaveGamePaths.Resolve(path);
        return Create(services, session => session.InitializeFromSaveAsync(resolvedPath));
    }

    public static string Save(GameSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(session);

        var resolvedPath = SaveGamePaths.Resolve(path);
        return session.Session.SaveAsync(resolvedPath).GetAwaiter().GetResult();
    }

    private static GameSession Create(
        IServiceProvider services,
        Func<SessionClient, Task<SessionInfo>> initialize)
    {
        var client = services.GetRequiredService<GameClient>();
        var engineProvider = services.GetRequiredService<ILocalEngineProvider>();

        var sessionId = Guid.NewGuid().ToString("N");
        var sessionClient = client.GetSession(sessionId);

        // Console host is sync throughout; blocking here is safe (no sync context).
        var info = initialize(sessionClient).GetAwaiter().GetResult();

        var engine = engineProvider.GetEngine(sessionId)
            ?? throw new InvalidOperationException(
                $"Session '{sessionId}' has no in-process engine. The console host requires the server to run in-process.");

        return new GameSession(engine, sessionClient, info.WorldName, sessionId, services);
    }
}
