using SimEngine.ConsoleHost.Systems;
using SimEngine.Game.Serialization;
using SimEngine.Game.Systems;
using SimEngine.State;
using SimEngine.State.Serialization;
using SimEngine.Systems;

namespace SimEngine.ConsoleHost.Game;

internal static class GameSessionFactory
{
    public static GameSession CreateNew(SimulationState state, DateTimeOffset startDate, ulong seed, string worldName)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);

        var engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = startDate,
                Seed = seed,
                InitialState = state,
                ComponentCodecs = GameCodecs.All,
            },
            CreateSystems());

        return new GameSession(engine, worldName);
    }

    public static string Save(GameSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(session);

        var resolvedPath = SaveGamePaths.Resolve(path);
        session.Engine.Save(resolvedPath);
        return resolvedPath;
    }

    public static GameSession Load(string path)
    {
        var resolvedPath = SaveGamePaths.Resolve(path);
        var engine = SimulationEngine.Load(resolvedPath, CreateSystems(), GameCodecs.All);
        return new GameSession(engine, SaveGamePaths.GetDisplayName(resolvedPath));
    }

    private static ISimulationSystem[] CreateSystems() =>
    [
        new HeartbeatSystem(),
        new PopulationSystem(),
        new EconomySystem(),
    ];
}
