using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimEngine.Client;
using SimEngine.Contracts;

namespace SimEngine.Server.Tests;

/// <summary>
/// Shared per-test host that stands up the SimEngine server actor topology in
/// local (single-silo) mode and exposes the pieces tests need: the in-process
/// <see cref="GameClient"/>, the raw session/player entity hosts for direct
/// <c>Ask</c> calls, and the <see cref="ILocalEngineProvider"/> for parity
/// checks. One instance per test keeps sessions isolated.
/// </summary>
public sealed class ServerTestHarness : IAsyncDisposable
{
    private readonly IHost _host;

    private ServerTestHarness(IHost host)
    {
        _host = host;
        System = host.Services.GetRequiredService<ActorSystem>();
        var registry = host.Services.GetRequiredService<ActorRegistry>();
        Sessions = registry.Get<SessionActorsMarker>();
        Players = registry.Get<PlayerActorsMarker>();
        Client = GameClient.FromLocalRegistry(System, registry);
        EngineProvider = host.Services.GetRequiredService<ILocalEngineProvider>();
    }

    /// <summary>The backing actor system.</summary>
    public ActorSystem System { get; }

    /// <summary>The game-session entity host.</summary>
    public IActorRef Sessions { get; }

    /// <summary>The player entity host.</summary>
    public IActorRef Players { get; }

    /// <summary>An in-process client facade over the server actors.</summary>
    public GameClient Client { get; }

    /// <summary>Direct access to hosted engines for parity assertions.</summary>
    public ILocalEngineProvider EngineProvider { get; }

    /// <summary>Builds and starts a fresh harness.</summary>
    public static async Task<ServerTestHarness> StartAsync(CancellationToken ct = default)
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSimEngineServer();
                services.AddAkka($"SimEngineTest{Guid.NewGuid():N}", (builder, _) =>
                {
                    builder.WithSimEngineActors(AkkaExecutionMode.LocalTest);
                });
            })
            .Build();

        await host.StartAsync(ct);
        return new ServerTestHarness(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
