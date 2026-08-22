using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SimEngine.Contracts;
using SimEngine.Server.Actors;

namespace SimEngine.Server;

/// <summary>
/// Akka.Hosting configuration for the SimEngine server actor topology: the
/// session entity host (the sole simulation authority), the player entity host,
/// and the singleton lobby. Works in both local single-player/test mode and in
/// clustered multiplayer mode.
/// </summary>
public static class SimEngineServerExtensions
{
    /// <summary>
    /// Registers the <see cref="ILocalEngineProvider"/> used by in-process
    /// clients to read engine state directly. Call before configuring Akka.
    /// </summary>
    public static IServiceCollection AddSimEngineServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ILocalEngineProvider, LocalEngineProvider>();
        return services;
    }

    /// <summary>
    /// Registers the SimEngine server actors on the Akka configuration builder.
    /// </summary>
    public static AkkaConfigurationBuilder WithSimEngineActors(
        this AkkaConfigurationBuilder builder,
        AkkaExecutionMode executionMode = AkkaExecutionMode.LocalTest,
        string? clusterRole = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (executionMode == AkkaExecutionMode.LocalTest)
        {
            builder.WithActors((system, registry, resolver) =>
            {
                var engineProvider = resolver.GetService<ILocalEngineProvider>();

                var sessions = system.ActorOf(
                    GenericChildPerEntityParent.CreateProps(
                        new SessionMessageExtractor(),
                        entityId => GameSessionActor.Props(entityId, engineProvider)),
                    ActorNames.Sessions);
                registry.Register<SessionActorsMarker>(sessions);

                var players = system.ActorOf(
                    GenericChildPerEntityParent.CreateProps(
                        new PlayerMessageExtractor(),
                        entityId => PlayerActor.Props(entityId, sessions)),
                    ActorNames.Players);
                registry.Register<PlayerActorsMarker>(players);

                var lobby = system.ActorOf(LobbyActor.Props(sessions), ActorNames.Lobby);
                registry.Register<LobbyMarker>(lobby);
            });
        }
        else
        {
            var shardOptions = new ShardOptions
            {
                StateStoreMode = StateStoreMode.DData,
                RememberEntities = false,
                Role = clusterRole,
            };

            builder
                .WithShardRegion<SessionActorsMarker>(
                    ActorNames.Sessions,
                    (_, _, resolver) =>
                    {
                        var engineProvider = resolver.GetService<ILocalEngineProvider>();
                        return entityId => GameSessionActor.Props(entityId, engineProvider);
                    },
                    new SessionMessageExtractor(),
                    shardOptions)
                .WithActors((system, registry, _) =>
                {
                    var sessions = registry.Get<SessionActorsMarker>();

                    var players = system.ActorOf(
                        GenericChildPerEntityParent.CreateProps(
                            new PlayerMessageExtractor(),
                            entityId => PlayerActor.Props(entityId, sessions)),
                        ActorNames.Players);
                    registry.Register<PlayerActorsMarker>(players);

                    var lobby = system.ActorOf(LobbyActor.Props(sessions), ActorNames.Lobby);
                    registry.Register<LobbyMarker>(lobby);
                });
        }

        return builder;
    }
}
