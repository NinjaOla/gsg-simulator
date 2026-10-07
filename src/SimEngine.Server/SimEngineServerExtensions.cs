using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using SimEngine.Contracts;
using SimEngine.Networking;
using SimEngine.Server.Actors;
using SimEngine.Server.Gateway;

namespace SimEngine.Server;

/// <summary>
/// Akka.Hosting configuration for the SimEngine server actor topology: the
/// session entity host (the sole simulation authority), the player entity host,
/// and the singleton lobby. Works in both local single-player/test mode and in
/// clustered multiplayer mode.
/// </summary>
public static class SimEngineServerExtensions
{
    private static int _gatewayCount;

    /// <summary>
    /// Registers the SimEngine server actors on the Akka configuration builder.
    /// </summary>
    public static AkkaConfigurationBuilder WithSimEngineActors(
        this AkkaConfigurationBuilder builder,
        AkkaExecutionMode executionMode = AkkaExecutionMode.LocalTest,
        string? clusterRole = null,
        SessionRuntimeOptions? sessionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = sessionOptions ?? SessionRuntimeOptions.Default;
        Props SessionProps(string entityId) => GameSessionActor.Props(entityId, options);

        if (executionMode == AkkaExecutionMode.LocalTest)
        {
            builder.WithActors((system, registry, _) =>
            {
                var sessions = system.ActorOf(
                    GenericChildPerEntityParent.CreateProps(
                        new SessionMessageExtractor(),
                        SessionProps),
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
                    (_, _, _) => SessionProps,
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

    /// <summary>
    /// Accepts client connections from the listener through a <see cref="GatewayActor"/>.
    /// Register after <see cref="WithSimEngineActors"/>. The gateway owns and disposes the listener.
    /// </summary>
    public static AkkaConfigurationBuilder WithSimEngineGateway(
        this AkkaConfigurationBuilder builder,
        ITransportListener listener,
        GatewayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(listener);
        var gatewayOptions = options ?? new GatewayOptions();
        // Unique per registration so a server can listen on several endpoints of one scheme.
        var name = $"gateway-{listener.LocalEndpoint.Scheme}-{Interlocked.Increment(ref _gatewayCount)}";

        builder.WithActors((system, registry, _) =>
        {
            var sessions = registry.Get<SessionActorsMarker>();
            var lobby = registry.Get<LobbyMarker>();
            system.ActorOf(GatewayActor.Props(listener, sessions, lobby, gatewayOptions), name);
        });

        return builder;
    }
}
